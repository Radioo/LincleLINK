using System.Data.Common;
using System.Text.Json;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Validation;
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
        var pendingKeys = new HashSet<string>(StringComparer.Ordinal);
        long pendingFileCount = 0;
        int handled = 0;

        // Snapshot the names already in the DB once (one query) instead of issuing a
        // round-trip per manifest. Nothing else writes during the migration, so the
        // snapshot stays current as long as each flush below updates it.
        var existingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var existing in await _repository.GetNamesAsync(ct))
        {
            existingKeys.Add(LincleLinkPersistence.NameKeyOf(existing));
        }

        for (var index = 0; index < files.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var file = files[index];
            var name = Path.GetFileNameWithoutExtension(file);

            try
            {
                var instance = await ReadInstanceAsync(file, ct);

                // Validate the inner name before it can reach the bulk insert: the
                // manifest file name can be valid while the inner Name is reserved or
                // otherwise invalid. Quarantining here keeps the bulk path intact
                // instead of degrading the whole chunk to per-instance writes.
                if (InstanceNameValidator.FirstError(instance.InstanceName) is { } validationError)
                {
                    throw new ArgumentException(validationError);
                }

                // Idempotent: a partially-run migration that already wrote this name
                // just discards the JSON on the next launch. The DB identity is the
                // inner Name, so the existence check uses its key, not the file name.
                var key = LincleLinkPersistence.NameKeyOf(instance.InstanceName);
                if (existingKeys.Contains(key))
                {
                    skipped++;
                    handled++;
                    TryDeleteLegacyFile(file, log);
                    ReportPercent(percent, handled, files.Count);
                    continue;
                }

                // Two manifests can collide on the inner name (identical, or case
                // variants on Linux) even when their file names differ. The snapshot
                // only sees the DB, not the current pending batch, so the duplicate
                // is quarantined here rather than tripping the primary key on insert.
                if (!pendingKeys.Add(key))
                {
                    quarantined++;
                    handled++;
                    var detail = $"{instance.InstanceName}: Duplicate manifest name; quarantined.";
                    errors.Add(detail);
                    log?.Report($"Quarantined {detail}");
                    Quarantine(file);
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
                foreach (var (instance, _) in pending)
                {
                    existingKeys.Add(LincleLinkPersistence.NameKeyOf(instance.InstanceName));
                }
                pending.Clear();
                pendingKeys.Clear();
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
                var detail = $"{instance.InstanceName}: Verification failed after writing.";
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
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or DbException)
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
                catch (Exception inner) when (inner is JsonException or IOException or UnauthorizedAccessException or ArgumentException or DbException)
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

    /// <summary>
    /// Deletes a migrated JSON manifest, tolerating a locked/read-only file (Windows
    /// AV scans, a file held open). The manifest stays in <c>instance/</c> and the
    /// idempotent skip handles it on the next launch, so one stuck file never aborts
    /// or misreports the rest of the migration.
    /// </summary>
    private void TryDeleteLegacyFile(string file, IProgress<string>? log)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Report($"Could not delete {Path.GetFileName(file)} ({ex.Message}); will retry next launch.");
        }
    }

    private static void ReportPercent(IProgress<double>? percent, int numerator, int denominator)
        => percent?.Report(100d * numerator / denominator);
}
