using System.Text.Json;
using FluentAssertions;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Instances;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Instances;

public sealed class JsonInstanceRepositoryTests : InstanceRepositoryContractTests
{
    protected override IInstanceRepository CreateRepository() => new JsonInstanceRepository(Paths);

    [Fact]
    public async Task Save_serializes_identically_to_v2_fixture()
    {
        var repo = (JsonInstanceRepository)CreateRepository();
        var instance = Instance.Create(
            "IIDX28",
            [new InstanceFile("25063_pre.2dx", @"sound\25063", 463806, "7AFE6AC1B80128D44BA5357D4349B21A.2dx")],
            [@"sound\25063"]);

        await repo.SaveAsync(instance);

        var written = await File.ReadAllTextAsync(Path.Combine(Paths.InstanceDirectory, "IIDX28.json"));
        Compact(written).Should().Be(Compact(TestData.V2InstanceJson));
    }

    [Fact]
    public async Task Get_roundtrips_v2_fixture_from_disk()
    {
        Directory.CreateDirectory(Paths.InstanceDirectory);
        await File.WriteAllTextAsync(Path.Combine(Paths.InstanceDirectory, "IIDX28.json"), TestData.V2InstanceJson);
        var repo = (JsonInstanceRepository)CreateRepository();

        var loaded = await repo.GetAsync("IIDX28");

        loaded.Should().NotBeNull();
        loaded!.InstanceName.Should().Be("IIDX28");
        loaded.FileList.Should().ContainSingle();
        loaded.FileList[0].RelativePath.Should().Be(@"sound\25063");
    }

    [Fact]
    public async Task Missing_collections_normalize_to_empty()
    {
        Directory.CreateDirectory(Paths.InstanceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(Paths.InstanceDirectory, "minimal.json"),
            """{ "Name": "minimal", "TotalFileSize": 0, "TotalFileCount": 0, "TotalFileSizeString": "0 B" }""");
        var repo = (JsonInstanceRepository)CreateRepository();

        var loaded = await repo.GetAsync("minimal");

        loaded.Should().NotBeNull();
        loaded!.FileList.Should().BeEmpty();
        loaded.DirectoryList.Should().BeEmpty();
    }

    [Fact]
    public async Task Corrupt_json_throws_InstanceStorageException()
    {
        Directory.CreateDirectory(Paths.InstanceDirectory);
        await File.WriteAllTextAsync(Path.Combine(Paths.InstanceDirectory, "bad.json"), "{ this is not json");
        var repo = (JsonInstanceRepository)CreateRepository();

        var act = () => repo.GetAsync("bad");
        await act.Should().ThrowAsync<InstanceStorageException>();
    }

    [Fact]
    public async Task Explicit_null_for_non_nullable_field_throws_InstanceStorageException()
    {
        Directory.CreateDirectory(Paths.InstanceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(Paths.InstanceDirectory, "nullname.json"),
            """{ "Name": null, "TotalFileSize": 0, "TotalFileCount": 0, "TotalFileSizeString": "0 B" }""");
        var repo = (JsonInstanceRepository)CreateRepository();

        var act = () => repo.GetAsync("nullname");
        await act.Should().ThrowAsync<InstanceStorageException>();
    }

    private static string Compact(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }
}
