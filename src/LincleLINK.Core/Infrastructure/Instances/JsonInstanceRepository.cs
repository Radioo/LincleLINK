using System.Text.Json;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Validation;
using LincleLINK.Core.Infrastructure.Collections;
using LincleLINK.Core.Infrastructure.Serialization;

namespace LincleLINK.Core.Infrastructure.Instances;

public sealed class JsonInstanceRepository : IInstanceRepository
{
    private readonly IAppPaths _paths;

    public JsonInstanceRepository(IAppPaths paths)
    {
        _paths = paths;
    }

    public Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<string>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(_paths.InstanceDirectory))
            {
                return [];
            }

            // Path.GetFileNameWithoutExtension returns null only for a trailing-separator
            // path, impossible here since every element comes from Directory.GetFiles.
            return Directory.GetFiles(_paths.InstanceDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f)!)
                .Order(NaturalStringComparer.Instance)
                .ToArray();
        }, ct);

    public async Task<IReadOnlyList<Instance>> GetAllAsync(CancellationToken ct = default)
    {
        var instances = new List<Instance>();
        foreach (var name in await GetNamesAsync(ct))
        {
            var instance = await GetAsync(name, ct);
            if (instance is not null)
            {
                instances.Add(instance);
            }
        }

        return instances;
    }

    public async Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in await GetAllAsync(ct))
        {
            foreach (var file in instance.FileList)
            {
                names.Add(file.HashedFileName);
            }
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<InstanceListEntry>> GetSummariesAsync(CancellationToken ct = default)
    {
        var summaries = new List<InstanceListEntry>();
        foreach (var name in await GetNamesAsync(ct))
        {
            var instance = await GetAsync(name, ct);
            if (instance is not null)
            {
                summaries.Add(InstanceListEntry.From(instance));
            }
        }

        return summaries;
    }

    public async Task<long> GetUniqueSizeAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);

        // In-memory equivalent of the SQLite GROUP BY: a hash counts when this
        // entry references it and no other entry does (dedup within the entry).
        var target = await GetAsync(name, ct);
        if (target is null)
        {
            return 0;
        }

        var others = new HashSet<string>(StringComparer.Ordinal);
        foreach (var other in await GetAllAsync(ct))
        {
            if (string.Equals(other.InstanceName, target.InstanceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in other.FileList)
            {
                others.Add(file.HashedFileName);
            }
        }

        long total = 0;
        var counted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in target.FileList)
        {
            if (!others.Contains(file.HashedFileName) && counted.Add(file.HashedFileName))
            {
                total += file.FileSize;
            }
        }

        return total;
    }

    public async Task<Instance?> GetAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(name);
        if (path is null)
        {
            return null;
        }

        try
        {
            await using var fs = File.OpenRead(path);
            var instance = await JsonSerializer.DeserializeAsync<Instance>(fs, InstanceJson.Options, ct);
            return InstanceJson.Normalize(instance ?? throw new JsonException("Instance JSON was null."));
        }
        catch (JsonException ex)
        {
            throw new InstanceStorageException($"Instance '{name}' could not be read from {path}.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        ct.ThrowIfCancellationRequested();

        // O(1) fast path for the common exact-case lookup.
        if (File.Exists(PathFor(name)))
        {
            return true;
        }

        if (!Directory.Exists(_paths.InstanceDirectory))
        {
            return false;
        }

        // Off the caller's context: the case-insensitive fallback scan can be slow
        // on large stores and ExistsAsync is awaited from the UI thread during
        // add-instance.
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Directory.GetFiles(_paths.InstanceDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
        }, ct);
    }

    public async Task SaveAsync(Instance instance, CancellationToken ct = default)
    {
        ValidateName(instance.InstanceName);
        ct.ThrowIfCancellationRequested();

        // Denormalized derived fields, recomputed on save (plan 02 D3) so a mutated
        // FileList never leaves the persisted totals stale.
        instance.RecomputeTotals();

        Directory.CreateDirectory(_paths.InstanceDirectory);

        var path = PathFor(instance.InstanceName);
        var tempPath = path + ".tmp";

        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, instance, InstanceJson.Options, ct);
            }

            await Task.Run(() => File.Move(tempPath, path, overwrite: true), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstanceStorageException($"Instance '{instance.InstanceName}' could not be written to {path}.", ex);
        }
    }

    public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        ct.ThrowIfCancellationRequested();

        var path = ResolvePath(name);
        if (path is null)
        {
            return Task.FromResult(false);
        }

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InstanceStorageException($"Instance '{name}' could not be deleted from {path}.", ex);
            }

            return true;
        }, ct);
    }

    public async Task BulkInsertAsync(IReadOnlyList<Instance> instances, CancellationToken ct = default)
    {
        foreach (var instance in instances)
        {
            await SaveAsync(instance, ct);
        }
    }

    public Task SetCustomLogoAsync(string name, string? logoSource, CancellationToken ct = default)
    {
        // JSON repository is superseded by SQLite; logo customization is SQLite-only.
        return Task.CompletedTask;
    }

    private string PathFor(string name) => Path.Combine(_paths.InstanceDirectory, name + ".json");

    /// <summary>
    /// Resolves the on-disk path for an instance name case-insensitively (matching
    /// <see cref="ExistsAsync"/>), so <c>ExistsAsync(name)==true</c> always implies
    /// <c>GetAsync(name)!=null</c> on case-sensitive filesystems (Linux).
    /// </summary>
    private string? ResolvePath(string name)
    {
        var exact = PathFor(name);
        if (File.Exists(exact))
        {
            return exact;
        }

        if (!Directory.Exists(_paths.InstanceDirectory))
        {
            return null;
        }

        var match = Directory.GetFiles(_paths.InstanceDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
        return match;
    }

    private static void ValidateName(string name)
    {
        if (InstanceNameValidator.FirstError(name) is { } error)
        {
            throw new ArgumentException(error, nameof(name));
        }
    }
}
