using System.Text;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Validation;
using LincleLINK.Core.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
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

    public async Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default)
    {
        // Column-only projection (no Includes, no domain materialization): the
        // unused-file scan needs the hashes alone, never the full file rows.
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var hashes = await context.InstanceFiles
            .AsNoTracking()
            .Select(f => f.HashedFileName)
            .Distinct()
            .ToListAsync(ct);

        return hashes.Order(StringComparer.Ordinal).ToArray();
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

    public async Task BulkInsertAsync(IReadOnlyList<Instance> instances, CancellationToken ct = default)
    {
        if (instances.Count == 0)
        {
            return;
        }

        foreach (var instance in instances)
        {
            ValidateName(instance.InstanceName);
            instance.RecomputeTotals();
        }

        // One transaction for the whole batch, raw multi-row INSERTs bypassing EF
        // change tracking (which is catastrophically slow for ~1M rows). WAL +
        // synchronous=NORMAL avoid the per-commit fsync cost; journal mode is
        // persistent in the DB file, memory DBs (tests) simply report "memory".
        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await pragma.ExecuteNonQueryAsync(ct);
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        foreach (var instance in instances)
        {
            await InsertInstanceAsync(connection, transaction, instance, ct);
            await InsertFilesAsync(connection, transaction, instance, ct);
            await InsertDirectoriesAsync(connection, transaction, instance, ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static async Task InsertInstanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Instance instance,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "Instances" ("InstanceName", "NameKey", "TotalFileSize", "TotalFileCount", "TotalFileSizeString")
            VALUES ($name, $key, $size, $count, $sizeString)
            """;
        command.Parameters.Add(new SqliteParameter("$name", instance.InstanceName));
        command.Parameters.Add(new SqliteParameter("$key", LincleLinkPersistence.NameKeyOf(instance.InstanceName)));
        command.Parameters.Add(new SqliteParameter("$size", instance.TotalFileSize));
        command.Parameters.Add(new SqliteParameter("$count", instance.TotalFileCount));
        command.Parameters.Add(new SqliteParameter("$sizeString", instance.TotalFileSizeString));
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts the file rows in chunked multi-row statements (~6 params per row,
    /// well under SQLite's 32,766-variable limit). Keeps <see cref="Ordinal"/>
    /// global to the instance so order survives chunk boundaries.
    /// </summary>
    private static async Task InsertFilesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Instance instance,
        CancellationToken ct)
    {
        const string prefix = "INSERT INTO \"InstanceFiles\" (\"InstanceName\", \"Ordinal\", \"FileName\", \"RelativePath\", \"FileSize\", \"HashedFileName\") VALUES ";
        var files = instance.FileList;

        for (var start = 0; start < files.Count; start += BulkChunkSize)
        {
            var count = Math.Min(BulkChunkSize, files.Count - start);
            var sql = new StringBuilder(prefix);
            var parameters = new SqliteParameter[count * 6];

            for (var i = 0; i < count; i++)
            {
                var file = files[start + i];
                var ordinal = start + i;
                if (i > 0)
                {
                    sql.Append(',');
                }

                sql.Append($"($n{i},$o{i},$f{i},$r{i},$z{i},$h{i})");
                parameters[i * 6 + 0] = new SqliteParameter($"$n{i}", instance.InstanceName);
                parameters[i * 6 + 1] = new SqliteParameter($"$o{i}", ordinal);
                parameters[i * 6 + 2] = new SqliteParameter($"$f{i}", file.FileName);
                parameters[i * 6 + 3] = new SqliteParameter($"$r{i}", file.RelativePath);
                parameters[i * 6 + 4] = new SqliteParameter($"$z{i}", file.FileSize);
                parameters[i * 6 + 5] = new SqliteParameter($"$h{i}", file.HashedFileName);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql.ToString();
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task InsertDirectoriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Instance instance,
        CancellationToken ct)
    {
        const string prefix = "INSERT INTO \"InstanceDirectories\" (\"InstanceName\", \"Ordinal\", \"Value\") VALUES ";
        var directories = instance.DirectoryList;

        for (var start = 0; start < directories.Count; start += BulkChunkSize)
        {
            var count = Math.Min(BulkChunkSize, directories.Count - start);
            var sql = new StringBuilder(prefix);
            var parameters = new SqliteParameter[count * 3];

            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sql.Append(',');
                }

                sql.Append($"($n{i},$o{i},$v{i})");
                parameters[i * 3 + 0] = new SqliteParameter($"$n{i}", instance.InstanceName);
                parameters[i * 3 + 1] = new SqliteParameter($"$o{i}", start + i);
                parameters[i * 3 + 2] = new SqliteParameter($"$v{i}", directories[start + i]);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql.ToString();
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private const int BulkChunkSize = 4000;

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
