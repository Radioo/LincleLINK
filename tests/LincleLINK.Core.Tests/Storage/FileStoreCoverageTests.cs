using FluentAssertions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Storage;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Storage;

/// <summary>
/// <see cref="FileStore"/> branches the shared tests miss: size queries, deleting
/// a file that does not exist, and listing/summing an empty or missing db dir.
/// </summary>
public sealed class FileStoreCoverageTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;
    private readonly FileStore _store;

    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.2dx";

    public FileStoreCoverageTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        _store = new FileStore(_paths);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task GetSize_returns_zero_for_missing_files()
    {
        _store.GetSize(HashA).Should().Be(0);
    }

    [Fact]
    public async Task GetSize_returns_length_for_stored_files()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("src.2dx", new byte[7]), HashA);

        _store.GetSize(HashA).Should().Be(7);
    }

    [Fact]
    public async Task Delete_missing_file_is_a_noop()
    {
        await _store.DeleteAsync(HashA);

        _store.Exists(HashA).Should().BeFalse();
    }

    [Fact]
    public async Task Empty_db_directory_lists_nothing_and_sums_zero()
    {
        Directory.CreateDirectory(_paths.DbDirectory);

        (await _store.GetAllHashedFileNamesAsync()).Should().BeEmpty();
        (await _store.GetTotalSizeAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Missing_db_directory_lists_nothing_and_sums_zero()
    {
        (await _store.GetAllHashedFileNamesAsync()).Should().BeEmpty();
        (await _store.GetTotalSizeAsync()).Should().Be(0);
    }
}
