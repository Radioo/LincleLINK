using LincleLINK.Core.Abstractions.Filesystem;

namespace LincleLINK.Core.Infrastructure.Filesystem;

public sealed class FileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public async Task CopyFileAsync(string source, string dest, bool overwrite, CancellationToken ct = default)
    {
        await using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var dst = new FileStream(
            dest,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        await src.CopyToAsync(dst, 81920, ct);
    }

    public Task MoveFileAsync(string source, string dest, bool overwrite, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        File.Move(source, dest, overwrite);
        return Task.CompletedTask;
    }

    public bool DeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IReadOnlyList<string> EnumerateFiles(string root, bool recursive)
        => Directory.GetFiles(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public IReadOnlyList<string> EnumerateDirectories(string root, bool recursive)
        => Directory.GetDirectories(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public Stream OpenRead(string path) => File.OpenRead(path);
}
