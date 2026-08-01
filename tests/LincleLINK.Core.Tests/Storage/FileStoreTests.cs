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
        _store = new FileStore(_paths, new TestHardLinker());
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task CopyToStore_copies_and_dedups()
    {
        var source = _temp.CreateFile("src.2dx", "hello"u8.ToArray());

        await _store.CopyToStoreAsync(source, HashA);
        _store.Exists(HashA).Should().BeTrue();

        await _store.CopyToStoreAsync(source, HashA);
        _store.GetPath(HashA).Should().Be(Path.Combine(_paths.DbDirectory, HashA));
        Directory.GetFiles(_paths.DbDirectory).Should().ContainSingle();
    }

    [Fact]
    public async Task MoveToStore_moves_and_removes_source()
    {
        var source = _temp.CreateFile("src.2dx", "hello"u8.ToArray());

        await _store.MoveToStoreAsync(source, HashA);
        _store.Exists(HashA).Should().BeTrue();
        File.Exists(source).Should().BeFalse();
    }

    [Fact]
    public async Task MoveToStore_dedup_leaves_source_in_place()
    {
        var sourceA = _temp.CreateFile("a.2dx", "hello"u8.ToArray());
        var sourceB = _temp.CreateFile("b.2dx", "hello"u8.ToArray());

        await _store.MoveToStoreAsync(sourceA, HashA);
        await _store.MoveToStoreAsync(sourceB, HashA); // same hash, already stored

        File.Exists(sourceB).Should().BeTrue();
        _store.Exists(HashA).Should().BeTrue();
    }

    [Fact]
    public async Task CopyOut_never_overwrites_existing_destination()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", "new"u8.ToArray()), HashA);
        var dest = _temp.CreateFile("dest.bin", "old"u8.ToArray());

        await _store.CopyOutAsync(HashA, dest);

        File.ReadAllBytes(dest).Should().Equal("old"u8.ToArray());
    }

    [Fact]
    public async Task CopyOut_copies_when_destination_missing()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", "hello"u8.ToArray()), HashA);
        var dest = Path.Combine(_temp.Root, "out.bin");

        await _store.CopyOutAsync(HashA, dest);

        File.ReadAllBytes(dest).Should().Equal("hello"u8.ToArray());
    }

    [Fact]
    public async Task LinkOut_delegates_to_hard_linker_and_reports_result()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", "hello"u8.ToArray()), HashA);
        var dest = Path.Combine(_temp.Root, "linked.bin");

        (await _store.LinkOutAsync(HashA, dest)).Should().BeTrue();

        var failingStore = new FileStore(_paths, new TestHardLinker { Result = false });
        (await failingStore.LinkOutAsync(HashA, dest)).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_removes_stored_file()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", "hello"u8.ToArray()), HashA);

        await _store.DeleteAsync(HashA);

        _store.Exists(HashA).Should().BeFalse();
    }

    [Fact]
    public async Task GetAllHashedFileNames_returns_stored_names()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("a.2dx", "a"u8.ToArray()), HashA);
        await _store.CopyToStoreAsync(_temp.CreateFile("b.2dx", "b"u8.ToArray()), HashB);

        var names = await _store.GetAllHashedFileNamesAsync();

        names.Should().BeEquivalentTo(HashA, HashB);
    }

    [Fact]
    public async Task GetTotalSize_sums_stored_files()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("a.2dx", new byte[3]), HashA);
        await _store.CopyToStoreAsync(_temp.CreateFile("b.2dx", new byte[7]), HashB);

        (await _store.GetTotalSizeAsync()).Should().Be(10);
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
