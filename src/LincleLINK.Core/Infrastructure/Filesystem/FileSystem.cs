using LincleLINK.Core.Abstractions.Filesystem;

namespace LincleLINK.Core.Infrastructure.Filesystem;

public sealed class FileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public bool DeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        => File.Move(sourcePath, destinationPath, overwrite);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public IReadOnlyList<FileSystemEntry> ListDirectory(string path)
    {
        var entries = new List<FileSystemEntry>();
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            var isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);

            // Symlinks and junctions report a target. Other reparse points (cloud
            // placeholder folders, for one) don't, and are ordinary folders here.
            var content = isDirectory ? null : ReadThrough(info) as FileInfo;
            entries.Add(new FileSystemEntry(
                info.FullName, info.Name, isDirectory, content?.Length ?? 0, content?.LastWriteTimeUtc ?? default, info.LinkTarget));
        }

        return entries;
    }

    /// <summary>
    /// A file link is read through, so its size and write time are the target's.
    /// A broken or cyclic link falls back to the link itself: one bad link must
    /// not fail the listing of its whole folder, and reading it later reports it.
    /// </summary>
    private static FileSystemInfo ReadThrough(FileSystemInfo info)
    {
        if (info.LinkTarget is null)
        {
            return info;
        }

        try
        {
            return info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo { Exists: true } target ? target : info;
        }
        catch (IOException)
        {
            return info;
        }
    }

    public IReadOnlyList<string> EnumerateFiles(string root, bool recursive)
        => Directory.GetFiles(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public IReadOnlyList<string> EnumerateDirectories(string root, bool recursive)
        => Directory.GetDirectories(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public string ReadAllText(string path) => File.ReadAllText(path);
}
