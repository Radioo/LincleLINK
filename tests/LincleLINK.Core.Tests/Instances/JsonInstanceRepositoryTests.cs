using System.Text.Json;
using FluentAssertions;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

public sealed class JsonInstanceRepositoryTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;

    public JsonInstanceRepositoryTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Save_then_Get_roundtrips()
    {
        var repo = new JsonInstanceRepository(_paths);
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
    public async Task Save_serializes_identically_to_v2_fixture()
    {
        var repo = new JsonInstanceRepository(_paths);
        var instance = Instance.Create(
            "IIDX28",
            [new InstanceFile("25063_pre.2dx", @"sound\25063", 463806, "7AFE6AC1B80128D44BA5357D4349B21A.2dx")],
            [@"sound\25063"]);

        await repo.SaveAsync(instance);

        var written = await File.ReadAllTextAsync(Path.Combine(_paths.InstanceDirectory, "IIDX28.json"));
        Compact(written).Should().Be(Compact(TestData.V2InstanceJson));
    }

    [Fact]
    public async Task Get_roundtrips_v2_fixture_from_disk()
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        await File.WriteAllTextAsync(Path.Combine(_paths.InstanceDirectory, "IIDX28.json"), TestData.V2InstanceJson);
        var repo = new JsonInstanceRepository(_paths);

        var loaded = await repo.GetAsync("IIDX28");

        loaded.Should().NotBeNull();
        loaded!.InstanceName.Should().Be("IIDX28");
        loaded.FileList.Should().ContainSingle();
        loaded.FileList[0].RelativePath.Should().Be(@"sound\25063");
    }

    [Fact]
    public async Task Get_missing_returns_null()
    {
        var repo = new JsonInstanceRepository(_paths);
        (await repo.GetAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task GetNames_returns_sorted_names()
    {
        var repo = new JsonInstanceRepository(_paths);
        await repo.SaveAsync(Instance.Create("beta", [], []));
        await repo.SaveAsync(Instance.Create("Alpha", [], []));
        await repo.SaveAsync(Instance.Create("charlie", [], []));

        var names = await repo.GetNamesAsync();
        names.Should().Equal("Alpha", "beta", "charlie");
    }

    [Fact]
    public async Task Exists_is_case_insensitive()
    {
        var repo = new JsonInstanceRepository(_paths);
        await repo.SaveAsync(Instance.Create("IIDX28", [], []));

        (await repo.ExistsAsync("iidx28")).Should().BeTrue();
        (await repo.ExistsAsync("IIDX28")).Should().BeTrue();
        (await repo.ExistsAsync("other")).Should().BeFalse();
    }

    [Fact]
    public async Task Get_and_Delete_resolve_case_insensitively()
    {
        var repo = new JsonInstanceRepository(_paths);
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
        var repo = new JsonInstanceRepository(_paths);
        await repo.SaveAsync(Instance.Create("IIDX28", [], []));

        (await repo.DeleteAsync("IIDX28")).Should().BeTrue();
        (await repo.ExistsAsync("IIDX28")).Should().BeFalse();
        (await repo.DeleteAsync("IIDX28")).Should().BeFalse();
    }

    [Fact]
    public async Task Save_recomputes_TotalFileSizeString()
    {
        var repo = new JsonInstanceRepository(_paths);
        var instance = Instance.Create("X", [new InstanceFile("f.bin", "", 1024, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin")], []);
        instance.TotalFileSizeString = "999 MB";

        await repo.SaveAsync(instance);
        var loaded = await repo.GetAsync("X");

        loaded!.TotalFileSizeString.Should().Be("1 KB");
    }

    [Fact]
    public async Task Missing_collections_normalize_to_empty()
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_paths.InstanceDirectory, "minimal.json"),
            """{ "Name": "minimal", "TotalFileSize": 0, "TotalFileCount": 0, "TotalFileSizeString": "0 B" }""");
        var repo = new JsonInstanceRepository(_paths);

        var loaded = await repo.GetAsync("minimal");

        loaded.Should().NotBeNull();
        loaded!.FileList.Should().BeEmpty();
        loaded.DirectoryList.Should().BeEmpty();
    }

    [Fact]
    public async Task Corrupt_json_throws_InstanceStorageException()
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        await File.WriteAllTextAsync(Path.Combine(_paths.InstanceDirectory, "bad.json"), "{ this is not json");
        var repo = new JsonInstanceRepository(_paths);

        var act = () => repo.GetAsync("bad");
        await act.Should().ThrowAsync<InstanceStorageException>();
    }

    [Fact]
    public async Task Explicit_null_for_non_nullable_field_throws_InstanceStorageException()
    {
        Directory.CreateDirectory(_paths.InstanceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_paths.InstanceDirectory, "nullname.json"),
            """{ "Name": null, "TotalFileSize": 0, "TotalFileCount": 0, "TotalFileSizeString": "0 B" }""");
        var repo = new JsonInstanceRepository(_paths);

        var act = () => repo.GetAsync("nullname");
        await act.Should().ThrowAsync<InstanceStorageException>();
    }

    [Fact]
    public async Task Save_rejects_names_with_path_chars()
    {
        var repo = new JsonInstanceRepository(_paths);
        var act = () => repo.SaveAsync(Instance.Create("a\\b", [], []));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static string Compact(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }
}
