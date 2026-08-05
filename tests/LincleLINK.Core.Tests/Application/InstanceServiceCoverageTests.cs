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
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// Remaining branches of <see cref="InstanceService"/>: game detection capture and
/// the link-original cleanup when the swap-over-the-original move fails.
/// </summary>
public sealed class InstanceServiceCoverageTests
{
    private static string Data => Path.Combine(Path.GetTempPath(), "data");
    private static string FileA => Path.Combine(Data, "a.bin");
    private static string StoreA => "C:\\db\\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin";

    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IGameVersionDetector _detector = Substitute.For<IGameVersionDetector>();

    private InstanceService CreateService() =>
        new(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, _detector);

    private void StubDataPath()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([FileA]);
        _fs.EnumerateDirectories(Data, true).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        _store.Exists(Arg.Any<string>()).Returns(false);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1_000_000_000_000L);
    }

    [Fact]
    public async Task Detected_game_is_captured_on_the_saved_instance()
    {
        StubDataPath();
        var info = new GameVersionInfo(
            "KFC", "SOUND VOLTEX", "J", "A", "1", "2013060500",
            "kfc-5a01c0a8_1000", null, "SDVX/SDVX_II_logo", DetectionConfidence.XmlAndPe);
        _detector.DetectAsync(Data, Arg.Any<CancellationToken>())
            .Returns(new DetectionResult(info, Data, "data", true));

        Instance? saved = null;
        _repository.SaveAsync(Arg.Do<Instance>(i => saved = i), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Copy), ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.DetectedGame.Should().BeSameAs(info);
    }

    [Fact]
    public async Task Move_file_failure_after_link_deletes_temp_link_and_rethrows()
    {
        StubDataPath();
        _store.GetPath(Arg.Any<string>()).Returns(StoreA);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });
        _fs.When(x => x.MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
            .Do(_ => throw new IOException("locked"));

        var act = () => CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Move));

        await act.Should().ThrowAsync<IOException>();
        _fs.Received(1).DeleteFile(Arg.Is<string>(p => p != null && p.EndsWith(".lincletmp", StringComparison.Ordinal)));
    }
}
