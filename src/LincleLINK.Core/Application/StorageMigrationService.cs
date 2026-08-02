using System.Text.Json;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Persistence;
using LincleLINK.Core.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LincleLINK.Core.Application;

public sealed record StorageMigrationResult(
    int Migrated,
    int Skipped,
    int Quarantined,
    IReadOnlyList<string> Errors);

/// <summary>
/// Forced, one-time JSON → SQLite migration (plan 13 §8). UI-free: the app hosts a
/// progress window and calls <see cref="MigrateAsync"/>. Reads instance manifests
/// from <c>instance/</c>, writes them through <see cref="IInstanceRepository"/>
/// (the registered SQLite implementation), deletes each JSON file after a verified
/// write, and quarantines unreadable files into <c>instance-corrupt/</c> so a
/// corrupt manifest never blocks the app or re-prompts forever.
/// </summary>
public class StorageMigrationService
{
    private const string QuarantineDirectoryName = "instance-corrupt";

    /// <summary>Flush pending manifests as one bulk insert every N instances or file rows.</summary>
    private const int BulkFlushThreshold = 50;
    private const long BulkFileFlushThreshold = 100_000;

    private readonly IAppPaths _paths;
    private readonly IInstanceRepository _repository;
    private readonly IDbContextFactory<LincleLinkDbContext> _contextFactory;

    public StorageMigrationService(
        IAppPaths paths,
        IInstanceRepository repository,
        IDbContextFactory<LincleLinkDbContext> contextFactory)
    {
        _paths = paths;
        _repository = repository;
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// True when any legacy <c>instance/*.json</c> manifest remains. New installs
    /// never write JSON, so presence of a JSON file is the migration trigger.
    /// </summary>
    public virtual bool NeedsMigration()
        => Directory.Exists(_paths.InstanceDirectory)
        && Directory.EnumerateFiles(_paths.InstanceDirectory, "*.json").Any();

    /// <summary>
    /// Applies any pending EF migrations so the schema exists before the first
    /// repository query (plan 13 §8 step 1). Called unconditionally at startup —
    /// fresh installs have no JSON to trigger <see cref="MigrateAsync"/> but still
    /// need the schema, otherwise the first SELECT fails on a missing table.
    /// </summary>
    public virtual async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        await context.Database.MigrateAsync(ct);
    }

    public virtual async Task<StorageMigrationResult> MigrateAsync(
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        if (!Directory.Exists(_paths.InstanceDirectory))
        {
            return new StorageMigrationResult(0, 0, 0, []);
        }

        var files = Directory.EnumerateFiles(_paths.InstanceDirectory, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            return new StorageMigrationResult(0, 0, 0, []);
        }

        int migrated = 0;
        int skipped = 0;
        int quarantined = 0;
        var errors = new List<string>();

        // Manifests are parsed and accumulated, then written in one bulk insert
        // (a single SQLite transaction) instead of one context+commit per instance.
        // Failure isolation is preserved: unreadable names are quarantined during
        // the parse phase, and a failed chunk falls back to per-instance writes.
        // The source file path travels with each instance so delete/quarantine uses
        // the actual manifest file, which can differ from the inner name.
        var pending = new List<(Instance Instance, string File)>();
        long pendingFileCount = 0;
        int handled = 0;

        for (var index = 0; index < files.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var file = files[index];
            var name = Path.GetFileNameWithoutExtension(file);

            try
            {
                var instance = await ReadInstanceAsync(file, ct);

                // Idempotent: a partially-run migration that already wrote this name
                // just discards the JSON on the next launch.
                if (await _repository.ExistsAsync(name, ct))
                {
                    skipped++;
                    handled++;
                    File.Delete(file);
                    ReportPercent(percent, handled, files.Count);
                    continue;
                }

                pending.Add((instance, file));
                pendingFileCount += instance.FileList.Count;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                quarantined++;
                handled++;
                var detail = $"{name}: {ex.Message}";
                errors.Add(detail);
                log?.Report($"Quarantined {detail}");
                Quarantine(file);
                ReportPercent(percent, handled, files.Count);
            }

            if (pending.Count >= BulkFlushThreshold || pendingFileCount >= BulkFileFlushThreshold)
            {
                var (m, q, e) = await FlushAsync(pending, handled, files.Count, log, percent, ct);
                migrated += m;
                quarantined += q;
                errors.AddRange(e);
                handled += pending.Count;
                pending.Clear();
                pendingFileCount = 0;
            }
        }

        if (pending.Count > 0)
        {
            var (m, q, e) = await FlushAsync(pending, handled, files.Count, log, percent, ct);
            migrated += m;
            quarantined += q;
            errors.AddRange(e);
        }

        log?.Report(
            $"Migration finished: {migrated} migrated, {skipped} already present, {quarantined} quarantined.");
        return new StorageMigrationResult(migrated, skipped, quarantined, errors);
    }

    /// <summary>
    /// Writes a pending batch via <see cref="IInstanceRepository.BulkInsertAsync"/>
    /// and finalizes each manifest (delete JSON / quarantine) with a cheap
    /// existence check instead of re-reading every file row. On a bulk failure,
    /// falls back to per-instance <see cref="IInstanceRepository.SaveAsync"/> so a
    /// single bad manifest is quarantined rather than stranding the whole batch.
    /// </summary>
    private async Task<(int Migrated, int Quarantined, IReadOnlyList<string> Errors)> FlushAsync(
        IReadOnlyList<(Instance Instance, string File)> pending,
        int handled,
        int totalFiles,
        IProgress<string>? log,
        IProgress<double>? percent,
        CancellationToken ct)
    {
        int migrated = 0;
        int quarantined = 0;
        var errors = new List<string>();

        async Task FinalizeAsync(Instance instance, string file, int withinFlush)
        {
            if (await _repository.ExistsAsync(instance.InstanceName, ct))
            {
                migrated++;
                File.Delete(file);
                log?.Report($"Migrated {instance.InstanceName}");
            }
            else
            {
                quarantined++;
                var detail = $"{instance.InstanceName}: Verification failed after writing {instance.InstanceName}.";
                errors.Add(detail);
                log?.Report($"Quarantined {detail}");
                Quarantine(file);
            }

            ReportPercent(percent, handled + withinFlush + 1, totalFiles);
        }

        try
        {
            await _repository.BulkInsertAsync(pending.Select(p => p.Instance).ToList(), ct);

            for (var i = 0; i < pending.Count; i++)
            {
                await FinalizeAsync(pending[i].Instance, pending[i].File, i);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            log?.Report($"Bulk insert failed ({ex.Message}); retrying individually.");

            for (var i = 0; i < pending.Count; i++)
            {
                var (instance, file) = pending[i];

                try
                {
                    await _repository.SaveAsync(instance, ct);
                    await FinalizeAsync(instance, file, i);
                }
                catch (Exception inner) when (inner is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    quarantined++;
                    var detail = $"{instance.InstanceName}: {inner.Message}";
                    errors.Add(detail);
                    log?.Report($"Quarantined {detail}");
                    Quarantine(file);
                    ReportPercent(percent, handled + i + 1, totalFiles);
                }
            }
        }

        return (migrated, quarantined, errors);
    }

    private async Task<Instance> ReadInstanceAsync(string file, CancellationToken ct)
    {
        await using var fs = File.OpenRead(file);
        var instance = await JsonSerializer.DeserializeAsync<Instance>(fs, InstanceJson.Options, ct)
            ?? throw new JsonException("Instance JSON was null.");
        return InstanceJson.Normalize(instance);
    }

    private void Quarantine(string file)
    {
        var quarantineDir = Path.Combine(_paths.InstanceDirectory, QuarantineDirectoryName);
        Directory.CreateDirectory(quarantineDir);
        File.Move(file, Path.Combine(quarantineDir, Path.GetFileName(file)), overwrite: true);
    }

    private static void ReportPercent(IProgress<double>? percent, int numerator, int denominator)
        => percent?.Report(100d * numerator / denominator);
}
