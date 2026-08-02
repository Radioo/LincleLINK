using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Abstractions.Instances;

public interface IInstanceRepository
{
    Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Instance>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Lightweight projection for list views: only the summary columns, never the
    /// file/directory rows. The UI list must not materialize the whole DB.
    /// </summary>
    Task<IReadOnlyList<InstanceListEntry>> GetSummariesAsync(CancellationToken ct = default);

    /// <summary>Returns null when the instance does not exist.</summary>
    Task<Instance?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Case-insensitive existence check on all platforms.</summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);

    Task SaveAsync(Instance instance, CancellationToken ct = default);
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Inserts a batch of instances (with children) in one operation. Used by the
    /// one-time JSON → SQLite migration; the caller guarantees the names do not
    /// already exist. Implementations may use a faster bulk path than
    /// <see cref="SaveAsync"/> (SQLite inserts without change tracking).
    /// </summary>
    Task BulkInsertAsync(IReadOnlyList<Instance> instances, CancellationToken ct = default);
}
