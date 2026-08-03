using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Application;

/// <summary>
/// Shared instance lookups for services that must fetch an instance and surface a
/// consistent not-found error. Keeps the "'Instance \"...\" not found.'" message and
/// the null-check shape in one place instead of four copied call sites.
/// </summary>
public static class InstanceLookup
{
    /// <summary>Fetches an instance by name, or returns a not-found error message.</summary>
    public static async Task<(Instance? Instance, string? Error)> GetAsync(
        IInstanceRepository repository,
        string instanceName,
        CancellationToken ct = default)
    {
        var instance = await repository.GetAsync(instanceName, ct);
        return instance is null
            ? (null, $"Library entry '{instanceName}' not found.")
            : (instance, null);
    }
}
