using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Tests.TestHelpers;
using LincleLINK.App.ViewModels;
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

namespace LincleLINK.App.Tests;

/// <summary>
/// Remaining <see cref="AddInstanceViewModel"/> branches: folder analysis (pre-flight,
/// size estimation, game detection), radio exclusivity, and operation error paths.
/// </summary>
public sealed class AddInstanceViewModelCoverageTests
{
    private static string Data => Path.Combine(Path.GetTempPath(), "data");
    private static string FileA => Path.Combine(Data, "a.bin");

    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly ITaskbarProgress _taskbarProgress = Substitute.For<ITaskbarProgress>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IGameVersionDetector _detector = Substitute.For<IGameVersionDetector>();

    private AddInstanceViewModel Create() =>
        new(
            new InstanceService(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, _detector, NullLogger<InstanceService>.Instance),
            _dialogs, _taskbarProgress, _fs, _preflight, NullLogger<AddInstanceViewModel>.Instance, _detector);

    private void StubDataPath()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([FileA]);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        _store.Exists(Arg.Any<string>()).Returns(false);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1_000_000_000_000L);
    }

    [Fact]
    public void Exposes_title_and_dialog_sizes()
    {
        var vm = Create();

        vm.Title.Should().Be("Add folder to library");
        vm.DialogSize.Width.Should().Be(560);
        vm.DialogMinSize.Width.Should().Be(480);
    }

    [Fact]
    public void Radio_choices_are_mutually_exclusive()
    {
        var vm = Create();

        vm.IsKeepChecked = true;
        vm.IsReclaimChecked.Should().BeFalse();

        vm.IsReclaimChecked = true;
        vm.IsKeepChecked.Should().BeFalse();
        vm.Mode.Should().Be(CopyMoveMode.Move);

        vm.IsKeepChecked = true;
        vm.Mode.Should().Be(CopyMoveMode.Copy);
    }

    [Fact]
    public async Task Browse_sets_the_data_path()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns(Data);
        var vm = Create();

        await vm.BrowseCommand.ExecuteAsync(null);

        vm.DataPath.Should().Be(Data);
    }

    [Fact]
    public async Task Cross_volume_analysis_disables_reclaim_and_switches_to_keep()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _preflight.CheckLinkTo(Data).Returns("The folder is on a different drive than storage.");
        var vm = Create();
        vm.IsReclaimChecked = true;

        vm.DataPath = Data;
        await AsyncWaits.AwaitUntilAsync(() => !vm.ReclaimAvailable);

        vm.ReclaimAvailable.Should().BeFalse();
        vm.CrossVolumeReason.Should().Contain("different drive");
        vm.IsKeepChecked.Should().BeTrue();
    }

    [Fact]
    public async Task Analysis_detects_game_and_exposes_data_folder_hint()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _detector.DetectAsync(Data, Arg.Any<CancellationToken>()).Returns(new DetectionResult(
            new GameVersionInfo("KFC", "SOUND VOLTEX", "2013060500",
                null, "SOUND VOLTEX II", "SDVX/SDVX_II_logo", DetectionConfidence.Xml),
            Data,
            "data",
            true));
        var vm = Create();

        vm.DataPath = Data;
        await AsyncWaits.AwaitUntilAsync(() => vm.DetectedGameText is not null);

        vm.DetectedGameText.Should().Contain("SOUND VOLTEX II");
        vm.IsGameRootDetected.Should().BeTrue();
        vm.DataFolderHint.Should().Be(Path.Combine(Data, "data"));

        vm.SwitchToDataFolderCommand.Execute(null);
        vm.DataPath.Should().Be(Path.Combine(Data, "data"));
    }

    [Fact]
    public async Task Superseded_detection_does_not_overwrite_a_newer_analysis()
    {
        StubDataPath();
        var other = Path.Combine(Path.GetTempPath(), "other");
        _fs.DirectoryExists(other).Returns(true);
        _fs.EnumerateFiles(other, true).Returns([FileA]);
        _fs.EnumerateFiles(other, false).Returns([FileA]);
        _fs.EnumerateDirectories(other, false).Returns([]);

        // The first analysis blocks on a stale detection; the superseding one
        // resolves to SOUND VOLTEX II immediately.
        var stale = new TaskCompletionSource<DetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _detector.DetectAsync(Data, Arg.Any<CancellationToken>()).Returns(stale.Task);
        _detector.DetectAsync(other, Arg.Any<CancellationToken>()).Returns(new DetectionResult(
            new GameVersionInfo("KFC", "SOUND VOLTEX", "2013060500",
                null, "SOUND VOLTEX II", "SDVX/SDVX_II_logo", DetectionConfidence.Xml),
            other, "data", true));
        var vm = Create();

        vm.DataPath = Data;
        // Let the first analysis reach the detection await (blocked on `stale`).
        await AsyncWaits.AwaitUntilAsync(() => _detector.ReceivedCalls().Count() >= 1);

        // Supersede; the newer analysis applies its result.
        vm.DataPath = other;
        await AsyncWaits.AwaitUntilAsync(() => vm.DetectedGameText is not null);
        vm.DetectedGameText.Should().Contain("SOUND VOLTEX II");

        // Completing the stale detection with a different game must be discarded
        // by the staleness re-check, never overwriting the newer result.
        stale.SetResult(new DetectionResult(
            new GameVersionInfo("LDJ", "beatmania IIDX", "2023101800",
                null, "beatmania IIDX 31 EPOLIS", "IIDX/AC_EPOLIS_logo", DetectionConfidence.Xml),
            Data, "data", true));

        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            vm.DetectedGameText.Should().NotContain("EPOLIS");
        }
    }

    [Fact]
    public async Task CreateInstance_service_error_shows_dialog()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([FileA]);
        _fs.EnumerateDirectories(Data, true).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new IOException("disk full")));
        var vm = Create();
        vm.InstanceName = "inst";
        vm.DataPath = Data;

        await vm.CreateInstanceCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ErrorAsync(Arg.Is<string>(m => m != null && m.Contains("disk full")), Arg.Any<string>());
        vm.CompletedSuccessfully.Should().BeFalse();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task CreateInstance_cancelled_operation_logs_without_dialog()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([FileA]);
        _fs.EnumerateDirectories(Data, true).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new OperationCanceledException()));
        var vm = Create();
        vm.InstanceName = "inst";
        vm.DataPath = Data;

        await vm.CreateInstanceCommand.ExecuteAsync(null);

        await _dialogs.DidNotReceiveWithAnyArgs().ErrorAsync(default!, default!);
        vm.LogLines.Should().Contain(l => l.Contains("Operation cancelled"));
    }

    [Fact]
    public async Task CreateInstance_soft_cancel_reports_cancelled_line()
    {
        StubDataPath();
        _driveInfo.GetAvailableFreeSpace(Data).Returns(0L);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var vm = Create();
        vm.InstanceName = "inst";
        vm.DataPath = Data;
        vm.IsKeepChecked = true; // copy mode so the low-disk path applies

        await vm.CreateInstanceCommand.ExecuteAsync(null);

        vm.LogLines.Should().Contain(l => l.Contains("Operation cancelled"));
        vm.CompletedSuccessfully.Should().BeFalse();
    }

    [Fact]
    public async Task Cancel_operation_requests_cancellation()
    {
        StubDataPath();
        var gate = new TaskCompletionSource();
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.Task;
                return "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            });
        var vm = Create();
        vm.InstanceName = "inst";
        vm.DataPath = Data;

        var run = vm.CreateInstanceCommand.ExecuteAsync(null);
        await AsyncWaits.AwaitUntilAsync(() => vm.IsBusy);
        vm.IsBusy.Should().BeTrue();

        vm.CancelOperationCommand.Execute(null);
        vm.StatusLine.Should().Be("Cancelling...");

        gate.SetResult();
        await run;
        vm.IsBusy.Should().BeFalse();
    }
}
