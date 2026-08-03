using FluentAssertions;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

/// <summary>
/// Shared behavioral contract for every <see cref="IInstanceRepository"/>
/// implementation (plan 13 §10): the JSON and SQLite backends must behave
/// identically for the operations the app depends on. Each concrete repository
/// test derives from this and adds its format-specific coverage.
/// </summary>
public abstract class InstanceRepositoryContractTests : IDisposable
{
    protected readonly TempDir Temp = new();
    protected readonly IAppPaths Paths;

    protected InstanceRepositoryContractTests()
    {
        Paths = new AppPaths(Path.Combine(Temp.Root, "data"));
    }

    public virtual void Dispose() => Temp.Dispose();

    protected abstract IInstanceRepository CreateRepository();

    [Fact]
    public async Task Save_then_Get_roundtrips()
    {
        var repo = CreateRepository();
        var instance = Instance.Create(
            "IIDX28",
            [new InstanceFile("25063_pre.2dx", @"sound\25063", 463806, "7AFE6AC1B80128D44BA5357D4349B21A.2dx")],
            [@"sound\25063"]);

        await repo.SaveAsync(instance);
        var loaded = await repo.GetAsync("IIDX28");

        loaded.Should().NotBeNull();
        loaded!.InstanceName.Should().Be("IIDX28");
        loaded.TotalFileSize.Should().Be(463806);
        loaded.TotalFileCount.Should().Be(1);
        loaded.TotalFileSizeString.Should().Be("452.94 KB");
        loaded.FileList.Should().ContainSingle().Which.HashedFileName.Should().Be("7AFE6AC1B80128D44BA5357D4349B21A.2dx");
        loaded.DirectoryList.Should().Equal(@"sound\25063");
    }

    [Fact]
    public async Task Save_preserves_file_and_directory_order()
    {
        var repo = CreateRepository();
        var files = new List<InstanceFile>
        {
            new("b.bin", "dir", 1, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"),
            new("a.bin", "dir", 2, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"),
            new("c.bin", "", 3, "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC.bin"),
        };
        var directories = new List<string> { "z", "a", "m" };

        await repo.SaveAsync(Instance.Create("order", files, directories));
        var loaded = await repo.GetAsync("order");

        loaded!.FileList.Select(f => f.FileName).Should().Equal("b.bin", "a.bin", "c.bin");
        loaded.DirectoryList.Should().Equal("z", "a", "m");
    }

    [Fact]
    public async Task Get_missing_returns_null()
    {
        var repo = CreateRepository();
        (await repo.GetAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task GetNames_returns_sorted_names()
    {
        var repo = CreateRepository();
        await repo.SaveAsync(Instance.Create("beta", [], []));
        await repo.SaveAsync(Instance.Create("Alpha", [], []));
        await repo.SaveAsync(Instance.Create("charlie", [], []));

        var names = await repo.GetNamesAsync();
        names.Should().Equal("Alpha", "beta", "charlie");
    }

    [Fact]
    public async Task GetAll_returns_all_instances_sorted()
    {
        var repo = CreateRepository();
        await repo.SaveAsync(Instance.Create("beta", [], []));
        await repo.SaveAsync(Instance.Create("Alpha", [], []));

        var all = await repo.GetAllAsync();
        all.Select(i => i.InstanceName).Should().Equal("Alpha", "beta");
    }

    [Fact]
    public async Task GetSummaries_returns_sorted_summaries_with_totals()
    {
        var repo = CreateRepository();
        await repo.SaveAsync(Instance.Create(
            "beta",
            [new InstanceFile("f.bin", "", 100, "F".PadRight(32, 'F') + ".bin")],
            ["dir"]));
        await repo.SaveAsync(Instance.Create("Alpha", [], []));

        var summaries = await repo.GetSummariesAsync();
        summaries.Select(s => s.InstanceName).Should().Equal("Alpha", "beta");
        summaries[1].FileCount.Should().Be(1);
        summaries[1].TotalFileSize.Should().Be(100);
    }

    [Fact]
    public async Task BulkInsert_adds_all_instances_with_children()
    {
        var repo = CreateRepository();
        await repo.BulkInsertAsync([
            Instance.Create("beta", [new InstanceFile("b.bin", "dir", 2, "B".PadRight(32, 'B') + ".bin")], ["dir"]),
            Instance.Create("Alpha", [], []),
        ]);

        (await repo.GetNamesAsync()).Should().Equal("Alpha", "beta");

        var loaded = await repo.GetAsync("beta");
        loaded.Should().NotBeNull();
        loaded!.FileList.Should().ContainSingle().Which.HashedFileName.Should().Be("B".PadRight(32, 'B') + ".bin");
        loaded.DirectoryList.Should().Equal("dir");

        var summaries = await repo.GetSummariesAsync();
        summaries.Single(s => s.InstanceName == "beta").TotalFileSize.Should().Be(2);
    }

    [Fact]
    public async Task GetAllHashedFileNames_returns_distinct_referenced_hashes()
    {
        var repo = CreateRepository();
        var sharedHash = "A".PadRight(32, 'A') + ".bin";
        var otherHash = "B".PadRight(32, 'B') + ".bin";
        await repo.SaveAsync(Instance.Create(
            "a",
            [new InstanceFile("x.bin", "", 1, sharedHash), new InstanceFile("y.bin", "", 2, otherHash)],
            []));
        await repo.SaveAsync(Instance.Create("b", [new InstanceFile("z.bin", "", 3, sharedHash)], []));

        var hashes = await repo.GetAllHashedFileNamesAsync();
        hashes.Should().Equal(sharedHash, otherHash);
    }

    [Fact]
    public async Task Exists_is_case_insensitive()
    {
        var repo = CreateRepository();
        await repo.SaveAsync(Instance.Create("IIDX28", [], []));

        (await repo.ExistsAsync("iidx28")).Should().BeTrue();
        (await repo.ExistsAsync("IIDX28")).Should().BeTrue();
        (await repo.ExistsAsync("other")).Should().BeFalse();
    }

    [Fact]
    public async Task Get_and_Delete_resolve_case_insensitively()
    {
        var repo = CreateRepository();
        await repo.SaveAsync(Instance.Create("IIDX28", [], []));

        var loaded = await repo.GetAsync("iidx28");
        loaded.Should().NotBeNull();
        loaded!.InstanceName.Should().Be("IIDX28");

        (await repo.DeleteAsync("iidx28")).Should().BeTrue();
        (await repo.ExistsAsync("IIDX28")).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_removes_manifest_and_reports_result()
    {
        var repo = CreateRepository();
        await repo.SaveAsync(Instance.Create("IIDX28", [], []));

        (await repo.DeleteAsync("IIDX28")).Should().BeTrue();
        (await repo.ExistsAsync("IIDX28")).Should().BeFalse();
        (await repo.DeleteAsync("IIDX28")).Should().BeFalse();
    }

    [Fact]
    public async Task Save_recomputes_TotalFileSizeString()
    {
        var repo = CreateRepository();
        var instance = Instance.Create("X", [new InstanceFile("f.bin", "", 1024, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")], []);
        instance.TotalFileSizeString = "999 MB";

        await repo.SaveAsync(instance);
        var loaded = await repo.GetAsync("X");

        loaded!.TotalFileSizeString.Should().Be("1 KB");
    }

    [Fact]
    public async Task Save_rejects_names_with_path_chars()
    {
        var repo = CreateRepository();
        var act = () => repo.SaveAsync(Instance.Create("a\\b", [], []));
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
