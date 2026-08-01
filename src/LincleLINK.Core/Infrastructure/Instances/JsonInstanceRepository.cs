using System.Text.Json;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Serialization;
using LincleLINK.Core.Storage;

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

            return Directory.GetFiles(_paths.InstanceDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileNameWithoutExtension(f)!)
                .Order(StringComparer.Ordinal)
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

    public async Task<Instance?> GetAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        ct.ThrowIfCancellationRequested();

        var path = PathFor(name);
        if (!File.Exists(path))
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

    public Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        ct.ThrowIfCancellationRequested();

        if (!Directory.Exists(_paths.InstanceDirectory))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(
            Directory.GetFiles(_paths.InstanceDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Any(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task SaveAsync(Instance instance, CancellationToken ct = default)
    {
        ValidateName(instance.Name);
        ct.ThrowIfCancellationRequested();

        // Denormalized derived field, recomputed on save (plan 02 D3).
        instance.TotalFileSizeString = SizeFormatter.Format(instance.TotalFileSize);

        Directory.CreateDirectory(_paths.InstanceDirectory);

        var path = PathFor(instance.Name);
        var tempPath = path + ".tmp";

        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(fs, instance, InstanceJson.Options, ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public Task<bool> DeleteAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);
        ct.ThrowIfCancellationRequested();

        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    private string PathFor(string name) => Path.Combine(_paths.InstanceDirectory, name + ".json");

    private static void ValidateName(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(['\\', '/']) >= 0 || name.Contains(".."))
        {
            throw new ArgumentException("Invalid instance name.", nameof(name));
        }
    }
}
