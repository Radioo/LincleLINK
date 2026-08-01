namespace LincleLINK.Core.Abstractions.Filesystem;

/// <summary>
/// Thin IO facade for application services. Repositories/store intentionally use
/// real System.IO (see plan 04); this facade exists so services are unit-testable.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    long GetFileLength(string path);
    Task CopyFileAsync(string source, string dest, bool overwrite, CancellationToken ct = default);
    Task MoveFileAsync(string source, string dest, bool overwrite, CancellationToken ct = default);
    bool DeleteFile(string path);

    bool DirectoryExists(string path);
    void CreateDirectory(string path);

    /// <summary>Returns full paths, optionally recursive.</summary>
    IReadOnlyList<string> EnumerateFiles(string root, bool recursive);
    IReadOnlyList<string> EnumerateDirectories(string root, bool recursive);

    /// <summary>Opens a read stream (used by streaming piece verification).</summary>
    Stream OpenRead(string path);
}
