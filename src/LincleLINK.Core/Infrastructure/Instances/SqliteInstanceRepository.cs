using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Validation;
using LincleLINK.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LincleLINK.Core.Infrastructure.Instances;

/// <summary>
/// EF Core (SQLite) implementation of <see cref="IInstanceRepository"/> (plan 13).
/// Backed by an <c>IDbContextFactory</c> so the singleton repository uses a
/// short-lived context per operation; instance metadata lives in
/// <c>&lt;DataDirectory&gt;/linclelinc.db</c> while the <c>db/</c> dedup store
/// remains flat files. Semantics mirror <see cref="JsonInstanceRepository"/>:
/// case-insensitive uniqueness, sorted names, totals recomputed on save.
/// </summary>
public sealed class SqliteInstanceRepository : IInstanceRepository
{
    private readonly IDbContextFactory<LincleLinkDbContext> _contextFactory;

    public SqliteInstanceRepository(IDbContextFactory<LincleLinkDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var names = await context.Instances.Select(x => x.InstanceName).ToListAsync(ct);
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<Instance>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entities = await context.Instances
            .Include(x => x.Files)
            .Include(x => x.Directories)
            .ToListAsync(ct);

        return entities
            .OrderBy(x => x.InstanceName, StringComparer.Ordinal)
            .Select(ToDomain)
            .ToList();
    }

    public async Task<IReadOnlyList<InstanceListEntry>> GetSummariesAsync(CancellationToken ct = default)
    {
        // No Include / no child rows: the list view only needs the persisted
        // totals, so this reads one row per instance instead of the whole DB.
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var summaries = await context.Instances
            .AsNoTracking()
            .Select(x => new InstanceListEntry(
                x.InstanceName,
                x.TotalFileCount,
                x.TotalFileSize,
                x.TotalFileSizeString))
            .ToListAsync(ct);

        return summaries
            .OrderBy(x => x.InstanceName, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<Instance?> GetAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        var key = LincleLinkPersistence.NameKeyOf(name);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Instances
            .Include(x => x.Files)
            .Include(x => x.Directories)
            .FirstOrDefaultAsync(x => x.NameKey == key, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        var key = LincleLinkPersistence.NameKeyOf(name);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        return await context.Instances.AnyAsync(x => x.NameKey == key, ct);
    }

    public async Task SaveAsync(Instance instance, CancellationToken ct = default)
    {
        ValidateName(instance.InstanceName);
        instance.RecomputeTotals();

        var key = LincleLinkPersistence.NameKeyOf(instance.InstanceName);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var existing = await context.Instances
            .Include(x => x.Files)
            .Include(x => x.Directories)
            .FirstOrDefaultAsync(x => x.NameKey == key, ct);

        if (existing is null)
        {
            context.Instances.Add(new InstanceEntity
            {
                InstanceName = instance.InstanceName,
                NameKey = key,
                TotalFileSize = instance.TotalFileSize,
                TotalFileCount = instance.TotalFileCount,
                TotalFileSizeString = instance.TotalFileSizeString,
                Files = MapFiles(instance, instance.InstanceName),
                Directories = MapDirectories(instance, instance.InstanceName),
            });
        }
        else
        {
            // Upsert under the case-insensitive identity: keep the canonical
            // InstanceName of the first-seen casing, refresh totals and replace the
            // ordered children (JSON-file-equivalent "atomic rewrite").
            existing.TotalFileSize = instance.TotalFileSize;
            existing.TotalFileCount = instance.TotalFileCount;
            existing.TotalFileSizeString = instance.TotalFileSizeString;

            context.InstanceFiles.RemoveRange(existing.Files);
            context.InstanceDirectories.RemoveRange(existing.Directories);
            existing.Files = MapFiles(instance, existing.InstanceName);
            existing.Directories = MapDirectories(instance, existing.InstanceName);
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        var key = LincleLinkPersistence.NameKeyOf(name);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var entity = await context.Instances.FirstOrDefaultAsync(x => x.NameKey == key, ct);
        if (entity is null)
        {
            return false;
        }

        context.Instances.Remove(entity);
        await context.SaveChangesAsync(ct);
        return true;
    }

    private static Instance ToDomain(InstanceEntity entity) => new()
    {
        InstanceName = entity.InstanceName,
        TotalFileSize = entity.TotalFileSize,
        TotalFileCount = entity.TotalFileCount,
        TotalFileSizeString = entity.TotalFileSizeString,
        FileList = entity.Files
            .OrderBy(f => f.Ordinal)
            .Select(f => new InstanceFile(f.FileName, f.RelativePath, f.FileSize, f.HashedFileName))
            .ToList(),
        DirectoryList = entity.Directories
            .OrderBy(d => d.Ordinal)
            .Select(d => d.Value)
            .ToList(),
    };

    private static List<InstanceFileEntity> MapFiles(Instance instance, string instanceName)
        => instance.FileList
            .Select((file, ordinal) => new InstanceFileEntity
            {
                InstanceName = instanceName,
                Ordinal = ordinal,
                FileName = file.FileName,
                RelativePath = file.RelativePath,
                FileSize = file.FileSize,
                HashedFileName = file.HashedFileName,
            })
            .ToList();

    private static List<InstanceDirectoryEntity> MapDirectories(Instance instance, string instanceName)
        => instance.DirectoryList
            .Select((value, ordinal) => new InstanceDirectoryEntity
            {
                InstanceName = instanceName,
                Ordinal = ordinal,
                Value = value,
            })
            .ToList();

    private static void ValidateName(string name)
    {
        if (InstanceNameValidator.FirstError(name) is { } error)
        {
            throw new ArgumentException(error, nameof(name));
        }
    }
}
