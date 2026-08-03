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

    public IReadOnlyList<string> EnumerateFiles(string root, bool recursive)
        => Directory.GetFiles(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public IReadOnlyList<string> EnumerateDirectories(string root, bool recursive)
        => Directory.GetDirectories(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public Stream OpenRead(string path) => File.OpenRead(path);
}
