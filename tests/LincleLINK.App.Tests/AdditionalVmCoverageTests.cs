using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Tests.TestHelpers;
using LincleLINK.App.ViewModels;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// The last uncovered branches of the app's view models: folder-analysis edge
/// cases, the base class theme hook, and operation outcome logging.
/// </summary>
public sealed class AdditionalVmCoverageTests
{
    private static string Data => Path.Combine(Path.GetTempPath(), "data");
    private static string FileA => Path.Combine(Data, "a.bin");
    private static string SubFile => Path.Combine(Data, "sub", "b.bin");

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

    private AddInstanceViewModel CreateAddVm() =>
        new(
            new InstanceService(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, _detector, NullLogger<InstanceService>.Instance),
            _dialogs, _taskbarProgress, _fs, _preflight, NullLogger<AddInstanceViewModel>.Instance, _detector);

    [Fact]
    public void ViewModelBase_default_theme_hook_is_empty()
    {
        var vm = new PlainVm();

        vm.SetTheme(AppTheme.Dark);

        vm.IsDarkTheme.Should().BeTrue();
    }

    private sealed class PlainVm : ViewModelBase
    {
    }

    [Fact]
    public async Task AddInstance_analysis_counts_files_across_subdirectories()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([Path.Combine(Data, "sub")]);
        _fs.EnumerateFiles(Path.Combine(Data, "sub"), false).Returns([SubFile]);
        _fs.EnumerateDirectories(Path.Combine(Data, "sub"), false).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _fs.GetFileLength(SubFile).Returns(200);
        var vm = CreateAddVm();

        vm.DataPath = Data;
        await AsyncWaits.AwaitUntilAsync(() => !vm.IsCalculatingSize);

        vm.EstimatedSizeText.Should().NotBeEmpty();
        vm.IsCalculatingSize.Should().BeFalse();
    }

    [Fact]
    public async Task AddInstance_analysis_swallows_io_errors_during_estimation()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([]);
        _fs.GetFileLength(FileA).Returns(_ => throw new IOException("unreadable"));
        var vm = CreateAddVm();

        vm.DataPath = Data;
        await AsyncWaits.AwaitUntilAsync(() => !vm.IsCalculatingSize);

        vm.IsCalculatingSize.Should().BeFalse();
    }

    [Fact]
    public void AddInstance_analysis_cancelled_when_superseded()
    {
        using var gate = new ManualResetEventSlim();
        using var preflightStarted = new ManualResetEventSlim();
        using var preflightReleased = new ManualResetEventSlim();
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _preflight.CheckLinkTo(Data).Returns(_ => { preflightStarted.Set(); gate.Wait(); preflightReleased.Set(); return null; });
        var vm = CreateAddVm();

        vm.DataPath = Data;
        // Wait until the first analysis's pre-flight is actually running (a
        // superseded Task.Run whose delegate never started would skip it).
        preflightStarted.Wait(5000, TestContext.Current.CancellationToken).Should().BeTrue();

        // Supersede the analysis before the pre-flight completes.
        vm.DataPath = Path.Combine(Path.GetTempPath(), "other");
        gate.Set();

        // The superseded analysis completes (its pre-flight returned after the
        // gate) without publishing anything; wait for that deterministically.
        preflightReleased.Wait(5000, TestContext.Current.CancellationToken).Should().BeTrue();

        vm.ReclaimAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task AddInstance_detection_without_display_title_uses_game_title()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        _detector.DetectAsync(Data, Arg.Any<CancellationToken>()).Returns(new DetectionResult(
            new GameVersionInfo("KFC", "SOUND VOLTEX", "2013060500",
                null, null, "SDVX/SDVX_II_logo", DetectionConfidence.Xml),
            Data, "data", false));
        var vm = CreateAddVm();

        vm.DataPath = Data;
        await AsyncWaits.AwaitUntilAsync(() => vm.DetectedGameText is not null);

        vm.DetectedGameText.Should().Contain("SOUND VOLTEX · KFC 2013060500");
    }

    [Fact]
    public async Task AddInstance_detection_cancellation_is_swallowed()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, false).Returns([FileA]);
        _fs.EnumerateDirectories(Data, false).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        var detection = new TaskCompletionSource<DetectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _detector.DetectAsync(Data, Arg.Any<CancellationToken>()).Returns(detection.Task);
        var vm = CreateAddVm();

        vm.DataPath = Data;
        // Let the analysis reach the detection await, then cancel it.
        await AsyncWaits.AwaitUntilAsync(() => _detector.ReceivedCalls().Any());
        detection.SetException(new OperationCanceledException());

        vm.DetectedGameText.Should().BeNull();
    }

    [Fact]
    public void AddInstance_browse_is_gated_on_busy()
    {
        var vm = CreateAddVm();

        vm.BrowseCommand.CanExecute(null).Should().BeTrue();
        vm.CreateInstanceCommand.CanExecute(null).Should().BeTrue();

        vm.IsBusy = true;
        vm.BrowseCommand.CanExecute(null).Should().BeFalse();
        vm.CreateInstanceCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task AddInstance_cancel_gate_only_when_an_operation_is_running()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([FileA]);
        _fs.EnumerateDirectories(Data, true).Returns([]);
        _fs.GetFileLength(FileA).Returns(100);
        var gate = new TaskCompletionSource();
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await gate.Task; return "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; });
        var vm = CreateAddVm();
        vm.InstanceName = "inst";
        vm.DataPath = Data;

        vm.CancelOperationCommand.CanExecute(null).Should().BeFalse();

        var run = vm.CreateInstanceCommand.ExecuteAsync(null);
        await AsyncWaits.AwaitUntilAsync(() => vm.CancelOperationCommand.CanExecute(null));

        vm.CancelOperationCommand.CanExecute(null).Should().BeTrue();
        vm.CancelOperationCommand.Execute(null);
        vm.StatusLine.Should().Be("Cancelling...");

        gate.SetResult();
        await run;
    }
}
