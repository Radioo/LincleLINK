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

    public virtual async Task<StorageMigrationResult> MigrateAsync(
        IProgress<string>? log = null,
        IProgress<double>? percent = null,
        CancellationToken ct = default)
    {
        // Apply any pending EF migrations before touching data (plan 13 §8 step 1).
        await using (var context = await _contextFactory.CreateDbContextAsync(ct))
        {
            await context.Database.MigrateAsync(ct);
        }

        if (!Directory.Exists(_paths.InstanceDirectory))
        {
            return new StorageMigrationResult(0, 0, 0, []);
        }

        var files = Directory.EnumerateFiles(_paths.InstanceDirectory, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        int migrated = 0;
        int skipped = 0;
        int quarantined = 0;
        var errors = new List<string>();

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
                    File.Delete(file);
                    continue;
                }

                await _repository.SaveAsync(instance, ct);

                var written = await _repository.GetAsync(instance.InstanceName, ct);
                if (written is null || written.FileList.Count != instance.FileList.Count)
                {
                    throw new IOException($"Verification failed after writing {name}.");
                }

                File.Delete(file);
                migrated++;
                log?.Report($"Migrated {name}");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                quarantined++;
                var detail = $"{name}: {ex.Message}";
                errors.Add(detail);
                log?.Report($"Quarantined {detail}");
                Quarantine(file);
            }

            percent?.Report(100d * (index + 1) / files.Count);
        }

        log?.Report(
            $"Migration finished: {migrated} migrated, {skipped} already present, {quarantined} quarantined.");
        return new StorageMigrationResult(migrated, skipped, quarantined, errors);
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
}
