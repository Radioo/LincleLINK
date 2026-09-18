using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;

namespace LincleLINK.Core.Application;

/// <summary>
/// A read-only file system that looks like an Instance deployed under
/// <see cref="Root"/>, as an Update plan would leave it (plan 16 D10). Nothing is
/// deployed: a file that stays is read from Storage, and one a Source supplies is
/// read from where the Source has it on disk. It exists so the game detector can
/// run over an Instance. Lookups ignore case, like Instance paths do (ADR 0001).
/// </summary>
public sealed class PlannedInstanceFileSystem : IFileSystem
{
    private readonly IFileSystem _disk;
    private readonly Dictionary<string, PlannedEntry> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Folder> _folders = new(StringComparer.Ordinal);

    public PlannedInstanceFileSystem(UpdatePlan plan, IFileStore store, IFileSystem disk)
    {
        _disk = disk;

        // Rooted, so the detector's walk up to parent folders ends at folders this
        // view doesn't have instead of wandering over the real disk.
        Root = Path.Combine(Path.GetPathRoot(Path.GetTempPath()) ?? "/", "linclelink-planned-instance");
        _folders[string.Empty] = new Folder(Root);

        foreach (var directory in plan.Directories)
        {
            GetOrAddFolder(PathNormalizer.Canonicalize(directory));
        }

        foreach (var planned in plan.Files.Where(f => f.IsInResult))
        {
            var directory = PathNormalizer.Canonicalize(planned.File.RelativePath);
            var folder = GetOrAddFolder(directory);
            var virtualPath = Path.Combine(folder.VirtualPath, planned.File.FileName);

            // Identical content is already in Storage; anything else a Source brings
            // may not be there before Apply, so it is read from the Source.
            var physicalPath = planned is { Source: { } source, Status: not PlannedStatus.Identical }
                ? source.FullPath
                : store.GetPath(planned.File.HashedFileName);

            folder.Files.Add(virtualPath);
            _files[Key(directory, planned.File.FileName)] = new PlannedEntry(physicalPath, planned.File.FileSize);
        }
    }

    /// <summary>The folder the Instance appears to be deployed in.</summary>
    public string Root { get; }

    public bool FileExists(string path) => TryKey(path, out var key) && _files.ContainsKey(key);

    public bool DirectoryExists(string path) => TryKey(path, out var key) && _folders.ContainsKey(key);

    public long GetFileLength(string path) => Entry(path).Length;

    public DateTime GetLastWriteTimeUtc(string path) => _disk.GetLastWriteTimeUtc(Entry(path).PhysicalPath);

    public Stream OpenRead(string path) => _disk.OpenRead(Entry(path).PhysicalPath);

    public string ReadAllText(string path) => _disk.ReadAllText(Entry(path).PhysicalPath);

    // A folder this view doesn't have lists as empty. The detector walks up to the
    // parents of its start folder, which here lie outside the Instance, and treats
    // a throwing listing as a fault worth a warning each time.
    public IReadOnlyList<string> EnumerateFiles(string root, bool recursive)
        => Below(root, recursive).SelectMany(f => f.Files).ToList();

    public IReadOnlyList<string> EnumerateDirectories(string root, bool recursive)
        => Below(root, recursive).SelectMany(f => f.Folders).Select(f => f.VirtualPath).ToList();

    public IReadOnlyList<FileSystemEntry> ListDirectory(string path)
    {
        var folder = FolderAt(path);
        return
        [
            .. folder.Folders.Select(f => new FileSystemEntry(f.VirtualPath, Path.GetFileName(f.VirtualPath), true, 0, default, null)),
            .. folder.Files.Select(f => new FileSystemEntry(f, Path.GetFileName(f), false, GetFileLength(f), GetLastWriteTimeUtc(f), null)),
        ];
    }

    public bool DeleteFile(string path) => throw ReadOnly();

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) => throw ReadOnly();

    public void CreateDirectory(string path) => throw ReadOnly();

    private static NotSupportedException ReadOnly() => new("A planned instance is read-only.");

    private IEnumerable<Folder> Below(string root, bool recursive)
    {
        if (!TryKey(root, out var key) || !_folders.TryGetValue(key, out var start))
        {
            yield break;
        }

        yield return start;
        if (!recursive)
        {
            yield break;
        }

        var pending = new Stack<Folder>(start.Folders);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            yield return folder;
            foreach (var child in folder.Folders)
            {
                pending.Push(child);
            }
        }
    }

    private Folder FolderAt(string path)
        => TryKey(path, out var key) && _folders.TryGetValue(key, out var folder)
            ? folder
            : throw new DirectoryNotFoundException($"'{path}' is not a folder of the planned instance.");

    private PlannedEntry Entry(string path)
        => TryKey(path, out var key) && _files.TryGetValue(key, out var entry)
            ? entry
            : throw new FileNotFoundException("Not a file of the planned instance.", path);

    private Folder GetOrAddFolder(string canonicalPath)
    {
        var key = canonicalPath.ToUpperInvariant();
        if (_folders.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var split = canonicalPath.LastIndexOf('/');
        var parent = GetOrAddFolder(split < 0 ? string.Empty : canonicalPath[..split]);
        var folder = new Folder(Path.Combine(parent.VirtualPath, canonicalPath[(split + 1)..]));
        parent.Folders.Add(folder);
        _folders[key] = folder;
        return folder;
    }

    /// <summary>Maps a path under <see cref="Root"/> to its lookup key; false for a path outside it.</summary>
    private bool TryKey(string path, out string key)
    {
        var relative = Path.GetRelativePath(Root, path);
        if (relative == ".")
        {
            key = string.Empty;
            return true;
        }

        var leavesRoot = relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        if (Path.IsPathRooted(relative) || leavesRoot)
        {
            key = string.Empty;
            return false;
        }

        key = PathNormalizer.Canonicalize(relative).ToUpperInvariant();
        return true;
    }

    private static string Key(string canonicalDirectory, string fileName)
        => (canonicalDirectory.Length == 0 ? fileName : $"{canonicalDirectory}/{fileName}").ToUpperInvariant();

    private sealed record PlannedEntry(string PhysicalPath, long Length);

    private sealed class Folder(string virtualPath)
    {
        public string VirtualPath { get; } = virtualPath;

        public List<Folder> Folders { get; } = [];

        public List<string> Files { get; } = [];
    }
}
