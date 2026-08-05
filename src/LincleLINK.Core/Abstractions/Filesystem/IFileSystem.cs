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

    /// <summary>
    /// Moves/renames a file. With <paramref name="overwrite"/> the destination is
    /// replaced (atomically when source and destination share a volume). Blocks.
    /// </summary>
    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    bool DirectoryExists(string path);
    void CreateDirectory(string path);

    /// <summary>Returns full paths, optionally recursive. Blocks.</summary>
    IReadOnlyList<string> EnumerateFiles(string root, bool recursive);
    IReadOnlyList<string> EnumerateDirectories(string root, bool recursive);

    /// <summary>Opens a read stream (used by streaming piece verification). Blocks.</summary>
    Stream OpenRead(string path);

    /// <summary>Reads all text from a file. Blocks.</summary>
    string ReadAllText(string path);
}
