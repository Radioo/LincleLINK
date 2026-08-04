using FluentAssertions;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Infrastructure.Filesystem;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Filesystem;

/// <summary>
/// Direct calls for every <see cref="FileSystem"/> wrapper method, including the
/// branches the services never reach (CreateDirectory, non-recursive enumeration,
/// move-overwrite, missing-file delete).
/// </summary>
public sealed class FileSystemDirectTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IFileSystem _fs = new FileSystem();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void FileExists_and_GetFileLength_work()
    {
        var path = _temp.CreateFile("a.bin", "hello"u8.ToArray());

        _fs.FileExists(path).Should().BeTrue();
        _fs.FileExists(Path.Combine(_temp.Root, "nope")).Should().BeFalse();
        _fs.GetFileLength(path).Should().Be(5);
    }

    [Fact]
    public void DeleteFile_returns_whether_it_deleted()
    {
        var path = _temp.CreateFile("a.bin");

        _fs.DeleteFile(path).Should().BeTrue();
        _fs.FileExists(path).Should().BeFalse();
        _fs.DeleteFile(path).Should().BeFalse();
    }

    [Fact]
    public void CreateDirectory_and_DirectoryExists_work()
    {
        var dir = Path.Combine(_temp.Root, "nested", "dir");

        _fs.CreateDirectory(dir);
        _fs.DirectoryExists(dir).Should().BeTrue();
    }

    [Fact]
    public void MoveFile_overwrites_existing_destination()
    {
        var source = _temp.CreateFile("src.txt", "new"u8.ToArray());
        var dest = _temp.CreateFile("dst.txt", "old"u8.ToArray());

        _fs.MoveFile(source, dest, overwrite: true);

        File.ReadAllText(dest).Should().Be("new");
    }

    [Fact]
    public void Enumerate_returns_immediate_children_when_not_recursive()
    {
        _temp.CreateFile("top.txt");
        _temp.CreateFile("sub/nested.txt");

        _fs.EnumerateFiles(_temp.Root, recursive: false).Should().ContainSingle(f => f.EndsWith("top.txt"));
        _fs.EnumerateFiles(_temp.Root, recursive: true).Should().HaveCount(2);
        _fs.EnumerateDirectories(_temp.Root, recursive: false).Should().ContainSingle();
        _fs.EnumerateDirectories(_temp.Root, recursive: true).Should().HaveCount(1);
    }

    [Fact]
    public void OpenRead_and_ReadAllText_return_contents()
    {
        var path = _temp.CreateFile("a.txt", "hello world"u8.ToArray());

        using var stream = _fs.OpenRead(path);
        stream.CanRead.Should().BeTrue();

        _fs.ReadAllText(path).Should().Be("hello world");
    }
}
