using FluentAssertions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Storage;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Storage;

public sealed class FileStoreTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;
    private readonly IFileStore _store;

    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.2dx";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.2dx";

    public FileStoreTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        _store = new FileStore(_paths);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task CopyToStore_copies_and_dedups()
    {
        var source = _temp.CreateFile("src.2dx", "hello"u8.ToArray());

        await _store.CopyToStoreAsync(source, HashA, TestContext.Current.CancellationToken);
        _store.Exists(HashA).Should().BeTrue();

        await _store.CopyToStoreAsync(source, HashA, TestContext.Current.CancellationToken);
        _store.GetPath(HashA).Should().Be(Path.Combine(_paths.DbDirectory, HashA));
        Directory.GetFiles(_paths.DbDirectory).Should().ContainSingle();
    }

    [Fact]
    public async Task CopyOut_never_overwrites_existing_destination()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", "new"u8.ToArray()), HashA, TestContext.Current.CancellationToken);
        var dest = _temp.CreateFile("dest.bin", "old"u8.ToArray());

        await _store.CopyFromStoreAsync(HashA, dest, TestContext.Current.CancellationToken);

        File.ReadAllBytes(dest).Should().Equal("old"u8.ToArray());
    }

    [Fact]
    public async Task CopyOut_copies_when_destination_missing()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", "hello"u8.ToArray()), HashA, TestContext.Current.CancellationToken);
        var dest = Path.Combine(_temp.Root, "out.bin");

        await _store.CopyFromStoreAsync(HashA, dest, TestContext.Current.CancellationToken);

        File.ReadAllBytes(dest).Should().Equal("hello"u8.ToArray());
    }

    [Fact]
    public async Task Delete_removes_stored_file()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", "hello"u8.ToArray()), HashA, TestContext.Current.CancellationToken);

        await _store.DeleteAsync(HashA, TestContext.Current.CancellationToken);

        _store.Exists(HashA).Should().BeFalse();
    }

    [Fact]
    public async Task GetAllHashedFileNames_returns_stored_names()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("a.2dx", "a"u8.ToArray()), HashA, TestContext.Current.CancellationToken);
        await _store.CopyToStoreAsync(_temp.CreateFile("b.2dx", "b"u8.ToArray()), HashB, TestContext.Current.CancellationToken);

        var names = await _store.GetAllHashedFileNamesAsync(TestContext.Current.CancellationToken);

        names.Should().BeEquivalentTo(HashA, HashB);
    }

    [Fact]
    public async Task GetTotalSize_sums_stored_files()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("a.2dx", new byte[3]), HashA, TestContext.Current.CancellationToken);
        await _store.CopyToStoreAsync(_temp.CreateFile("b.2dx", new byte[7]), HashB, TestContext.Current.CancellationToken);

        (await _store.GetTotalSizeAsync(TestContext.Current.CancellationToken)).Should().Be(10);
    }

    [Theory]
    [InlineData(@"..\evil")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("nothex.2dx")]
    public void Invalid_hash_names_are_rejected(string name)
    {
        var act = () => _store.GetPath(name);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
