namespace LincleLINK.Core.Abstractions.Filesystem;

/// <summary>
/// Thin IO facade for application services. Repositories/store intentionally use
/// real System.IO (see plan 04); this facade exists so services are unit-testable.
/// All members are synchronous and block the calling thread (they run on the
/// caller's context); the async boundary lives at the service layer, which hops to
/// the thread pool via <c>Task.Run</c> when an operation must not block the UI.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    long GetFileLength(string path);
    bool DeleteFile(string path);

    bool DirectoryExists(string path);
    void CreateDirectory(string path);

    /// <summary>Returns full paths, optionally recursive. Blocks.</summary>
    IReadOnlyList<string> EnumerateFiles(string root, bool recursive);
    IReadOnlyList<string> EnumerateDirectories(string root, bool recursive);

    /// <summary>Opens a read stream (used by streaming piece verification). Blocks.</summary>
    Stream OpenRead(string path);
}
