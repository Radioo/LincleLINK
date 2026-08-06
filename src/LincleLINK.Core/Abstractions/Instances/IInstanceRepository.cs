using LincleLINK.Core.Domain;

namespace LincleLINK.Core.Abstractions.Instances;

public interface IInstanceRepository
{
    Task<IReadOnlyList<string>> GetNamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Instance>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// All referenced dedup file hashes (distinct), one row per hashed file. The
    /// unused-file scan only needs the <c>HashedFileName</c> column; loading full
    /// instances here would materialize every file row in the DB.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// Lightweight projection for list views: only the summary columns, never the
    /// file/directory rows. The UI list must not materialize the whole DB.
    /// </summary>
    Task<IReadOnlyList<InstanceListEntry>> GetSummariesAsync(CancellationToken ct = default);

    /// <summary>Returns null when the instance does not exist.</summary>
    Task<Instance?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Case-insensitive existence check on all platforms.</summary>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Total bytes of dedup files referenced by this instance and by no other -
    /// what the storage cleanup could reclaim after removing the entry (plan 15
    /// D2, the inspector's "Unique to this entry" figure). Returns 0 for an
    /// unknown name. Duplicated hashes within the same instance count once.
    /// </summary>
    Task<long> GetUniqueSizeAsync(string name, CancellationToken ct = default);

    Task SaveAsync(Instance instance, CancellationToken ct = default);
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Inserts a batch of instances (with children) in one operation. Used by the
    /// one-time JSON → SQLite migration; the caller guarantees the names do not
    /// already exist. Implementations may use a faster bulk path than
    /// <see cref="SaveAsync"/> (SQLite inserts without change tracking).
    /// </summary>
    Task BulkInsertAsync(IReadOnlyList<Instance> instances, CancellationToken ct = default);

    Task SetCustomLogoAsync(string name, string? logoSource, CancellationToken ct = default);
}
