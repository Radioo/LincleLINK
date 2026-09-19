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

    /// <summary>
    /// Size in bytes of a stored file, or 0 when it does not exist. A single stat
    /// call; callers summing many files should do so off the UI thread.
    /// </summary>
    long GetSize(string hashedFileName);

    /// <summary>
    /// Copies a source file into the store; no-op when the hash already exists (dedup). Off-thread.
    /// The file appears under its hash name only once it is complete: a cancelled or
    /// failed copy leaves nothing there, since dedup would trust a partial file as that content.
    /// </summary>
    Task CopyToStoreAsync(string sourcePath, string hashedFileName, CancellationToken ct = default);

    /// <summary>Copies a stored file out of the store; never overwrites an existing destination. Off-thread.</summary>
    Task CopyFromStoreAsync(string hashedFileName, string destinationPath, CancellationToken ct = default);

    Task DeleteAsync(string hashedFileName, CancellationToken ct = default);

    /// <summary>
    /// The names of the stored content, and nothing else that may sit in <c>db/</c>
    /// (temp files of interrupted copies, foreign files). Every name returned is valid
    /// for <see cref="GetSize"/> and <see cref="DeleteAsync"/>.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes the temp files that copies interrupted by a crash left in <c>db/</c>
    /// and returns how many went. Must not run while a copy is in progress. Off-thread.
    /// </summary>
    Task<int> DeleteLeftoverTempFilesAsync(CancellationToken ct = default);
    Task<long> GetTotalSizeAsync(CancellationToken ct = default);
}
