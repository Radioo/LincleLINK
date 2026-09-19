namespace LincleLINK.Core.Abstractions.Filesystem;

/// <summary>
/// One child of a directory as <see cref="IFileSystem.ListDirectory"/> reports it.
/// </summary>
/// <param name="Length">Size of a file (its target's, for a file link); 0 for a directory.</param>
/// <param name="LinkTarget">Where a symlink or junction points; null for an ordinary entry.</param>
public sealed record FileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Length,
    DateTime LastWriteTimeUtc,
    string? LinkTarget);
