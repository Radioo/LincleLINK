using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// <see cref="InstanceService.DuplicateInstanceAsync"/> (plan 16 D3): a new entry
/// with the same files, directories, detected game and logo, and no Storage writes.
/// </summary>
public sealed class InstanceServiceDuplicateTests
{
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();

    private InstanceService CreateService() => new(
        Substitute.For<IFileSystem>(), Substitute.For<IFileHasher>(), _store, Substitute.For<IHardLinker>(),
        Substitute.For<IHardLinkPreflight>(), _repository, Substitute.For<IDriveInfoProvider>(),
        Substitute.For<IDialogService>(), Substitute.For<IGameVersionDetector>(), NullLogger<InstanceService>.Instance);

    private static Instance Source()
    {
        var source = Instance.Create(
            "IIDX 32",
            [new InstanceFile("bm2dx.dll", "modules", 2048, "AA.dll"), new InstanceFile("readme.txt", "", 1024, "BB.txt")],
            ["modules", @"data\tmp"]);
        source.DetectedGame = new GameVersionInfo("LDJ", "beatmania IIDX", "2026031800", null, null, "IIDX/x", DetectionConfidence.Xml);
        source.CustomLogoSource = "IIDX/y";
        return source;
    }

    [Fact]
    public async Task Duplicate_saves_a_new_entry_with_the_same_content_and_leaves_storage_alone()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Source());
        Instance? saved = null;
        await _repository.SaveAsync(Arg.Do<Instance>(i => saved = i), Arg.Any<CancellationToken>());

        var result = await CreateService().DuplicateInstanceAsync(
            "IIDX 32", "IIDX 32 - copy", ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.InstanceName.Should().Be("IIDX 32 - copy");
        saved.FileList.Should().Equal(Source().FileList);
        saved.DirectoryList.Should().Equal("modules", @"data\tmp");
        saved.TotalFileSize.Should().Be(3072);
        saved.DetectedGame.Should().Be(Source().DetectedGame);
        saved.CustomLogoSource.Should().Be("IIDX/y");
        _store.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad:name")]
    public async Task Duplicate_rejects_an_invalid_name(string newName)
    {
        var result = await CreateService().DuplicateInstanceAsync("IIDX 32", newName, ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_rejects_a_name_that_is_already_taken()
    {
        _repository.ExistsAsync("IIDX 33", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().DuplicateInstanceAsync("IIDX 32", "IIDX 33", ct: TestContext.Current.CancellationToken);

        result.Error.Should().Be("A library entry with this name already exists.");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_reports_a_missing_source()
    {
        _repository.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((Instance?)null);

        var result = await CreateService().DuplicateInstanceAsync("gone", "new", ct: TestContext.Current.CancellationToken);

        result.Error.Should().Be("Library entry 'gone' not found.");
    }
}
