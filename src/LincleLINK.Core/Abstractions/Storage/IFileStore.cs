namespace LincleLINK.Core.Abstractions.Storage;

/// <summary>
/// The deduplicated <c>db/</c> store. Hashed file names are validated against
/// <c>^[0-9A-F]{32}(\.[^\\/]+)?$</c> before any filesystem access.
/// </summary>
public interface IFileStore
{
    bool Exists(string hashedFileName);
    string GetPath(string hashedFileName);

    /// <summary>Copies a source file into the store; no-op when the hash already exists (dedup).</summary>
    Task CopyToStoreAsync(string sourcePath, string hashedFileName, CancellationToken ct = default);

    /// <summary>Copies a stored file out; never overwrites an existing destination.</summary>
    Task CopyOutAsync(string hashedFileName, string destinationPath, CancellationToken ct = default);

    /// <summary>Hard-links a stored file to a destination. Returns false on failure.</summary>
    Task<bool> LinkOutAsync(string hashedFileName, string destinationPath, CancellationToken ct = default);

    Task DeleteAsync(string hashedFileName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAllHashedFileNamesAsync(CancellationToken ct = default);
    Task<long> GetTotalSizeAsync(CancellationToken ct = default);
}
