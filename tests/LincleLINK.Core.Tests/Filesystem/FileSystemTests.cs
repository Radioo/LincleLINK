using FluentAssertions;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Filesystem;

public sealed class FileSystemTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IFileSystem _fs = new FileSystem();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void EnumerateFiles_is_recursive_and_returns_full_paths()
    {
        _temp.CreateFile("a.txt");
        _temp.CreateFile("sub/b.txt");
        _temp.CreateFile("sub/deep/c.txt");

        var files = _fs.EnumerateFiles(_temp.Root, recursive: true);

        files.Should().HaveCount(3);
        files.Should().Contain(f => f.EndsWith(Path.Combine("sub", "deep", "c.txt")));
    }

    [Fact]
    public void ListDirectory_returns_only_direct_children_with_sizes_and_write_times()
    {
        var file = _temp.CreateFile("a.txt", [1, 2, 3, 4, 5]);
        _temp.CreateFile("sub/b.txt");

        var entries = _fs.ListDirectory(_temp.Root);

        entries.Select(e => (e.Name, e.IsDirectory, e.LinkTarget)).Should().BeEquivalentTo(
            [("a.txt", false, (string?)null), ("sub", true, null)]);
        var listed = entries.Single(e => !e.IsDirectory);
        listed.FullPath.Should().Be(file);
        listed.Length.Should().Be(new FileInfo(file).Length);
        listed.LastWriteTimeUtc.Should().Be(_fs.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void ListDirectory_reports_a_directory_link_without_following_it()
    {
        _temp.CreateFile("real/inside.txt");
        var link = Path.Combine(_temp.Root, "link");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_temp.Root, "real"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows without Developer Mode can't create symlinks; nothing to check then.
            return;
        }

        var entry = _fs.ListDirectory(_temp.Root).Single(e => e.Name == "link");

        entry.IsDirectory.Should().BeTrue();
        entry.LinkTarget.Should().NotBeNull();
    }

    [Fact]
    public void EnumerateDirectories_is_recursive()
    {
        _temp.CreateFile("a/b/c.txt");

        var dirs = _fs.EnumerateDirectories(_temp.Root, recursive: true);

        dirs.Should().Contain(d => d.EndsWith(Path.Combine("a", "b")));
    }

    [Fact]
    public void DeleteFile_returns_whether_it_existed()
    {
        var path = _temp.CreateFile("f.txt");

        _fs.DeleteFile(path).Should().BeTrue();
        _fs.DeleteFile(path).Should().BeFalse();
    }

    [Fact]
    public void OpenRead_returns_file_contents()
    {
        var path = _temp.CreateFile("f.txt", "hello"u8.ToArray());

        using var stream = _fs.OpenRead(path);
        using var reader = new StreamReader(stream);
        reader.ReadToEnd().Should().Be("hello");
    }

    [Fact]
    public void GetFileLength_returns_byte_count()
    {
        var path = _temp.CreateFile("f.bin", new byte[42]);
        _fs.GetFileLength(path).Should().Be(42);
    }
}
