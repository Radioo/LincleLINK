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
    public void EnumerateDirectories_is_recursive()
    {
        _temp.CreateFile("a/b/c.txt");

        var dirs = _fs.EnumerateDirectories(_temp.Root, recursive: true);

        dirs.Should().Contain(d => d.EndsWith(Path.Combine("a", "b")));
    }

    [Fact]
    public async Task CopyFile_overwrite_semantics()
    {
        var src = _temp.CreateFile("src.txt", "new"u8.ToArray());
        var dest = _temp.CreateFile("dest.txt", "old"u8.ToArray());

        var actNoOverwrite = () => _fs.CopyFileAsync(src, dest, overwrite: false);
        await actNoOverwrite.Should().ThrowAsync<IOException>();

        await _fs.CopyFileAsync(src, dest, overwrite: true);
        File.ReadAllText(dest).Should().Be("new");
    }

    [Fact]
    public async Task MoveFile_with_overwrite()
    {
        var src = _temp.CreateFile("src.txt", "data"u8.ToArray());
        var dest = Path.Combine(_temp.Root, "dest.txt");

        await _fs.MoveFileAsync(src, dest, overwrite: true);

        _fs.FileExists(src).Should().BeFalse();
        _fs.FileExists(dest).Should().BeTrue();
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
