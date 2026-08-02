namespace LincleLINK.Core.Abstractions.Storage;

/// <summary>
/// The deduplicated <c>db/</c> store. Hashed file names are validated against
/// <c>^[0-9A-F]{32}(\.[^\\/]+)?$</c> before any filesystem access.
///
/// Sync vs async split: the two existence/path lookups (<see cref="Exists"/>,
/// <see cref="GetPath"/>) are cheap, non-blocking-in-practice single-path checks;
/// the Task-returning members that touch the disk (copy, delete, enumerate, size)
/// run their I/O off the caller's thread. Callers on the UI thread should await the
/// Task-returning members.
/// </summary>
public interface IFileStore
{
    /// <summary>Single-path existence check; safe to call from any thread.</summary>
    bool Exists(string hashedFileName);
    string GetPath(string hashedFileName);

    /// <summary>Copies a source file into the store; no-op when the hash already exists (dedup). Off-thread.</summary>
    Task CopyToStoreAsync(string sourcePath, string hashedFileName, CancellationToken ct = default);

    /// <summary>Copies a stored file out of the store; never overwrites an existing destination. Off-thread.</summary>
    Task CopyFromStoreAsync(string hashedFileName, string destinationPath, CancellationToken ct = default);

    Task DeleteAsync(string hashedFileName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default);
    Task<long> GetTotalSizeAsync(CancellationToken ct = default);
}
