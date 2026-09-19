using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Logos;
using LincleLINK.App.Services;
using LincleLINK.App.Tests.TestHelpers;
using LincleLINK.App.ViewModels;
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
/// Everything that takes time shows a progress indicator (CLAUDE.md). The shell's
/// activity bar is the indicator of every library operation, so it has to say what
/// runs, what that is doing right now, and never sit at a dead 0% or 100%.
/// </summary>
public sealed class OperationFeedbackTests
{
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IAppPaths _paths = Substitute.For<IAppPaths>();
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
    private readonly ITaskbarProgress _taskbarProgress = Substitute.For<ITaskbarProgress>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IGameVersionDetector _detector = Substitute.For<IGameVersionDetector>();

    private MainViewModel CreateViewModel()
    {
        _paths.DataDirectory.Returns(Path.Combine(Path.GetTempPath(), "linclelink-feedback-tests"));
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var instanceService = new InstanceService(
            _fs, _hasher, _store, Substitute.For<IHardLinker>(), _preflight, _repository, _driveInfo, _dialogs, _detector,
            NullLogger<InstanceService>.Instance);
        return new MainViewModel(
            instanceService,
            new LinkingService(_fs, _store, Substitute.For<IHardLinker>(), _preflight, _repository, _dialogs, NullLogger<LinkingService>.Instance),
            new UnusedFilesService(_store, _repository, _dialogs, NullLogger<UnusedFilesService>.Instance),
            new LegacyImporter(_repository, NullLogger<LegacyImporter>.Instance),
            new TorrentService(Substitute.For<ITorrentSource>(), _repository, _store, Substitute.For<IHardLinker>(), _fs, NullLogger<TorrentService>.Instance),
            _repository,
            new StatusService(_store, _repository, _driveInfo, _paths, NullLogger<StatusService>.Instance),
            _dialogs,
            Substitute.For<IThemeManager>(),
            _settingsStore,
            _taskbarProgress,
            _preflight,
            () => throw new InvalidOperationException("not exercised here"),
            () => throw new InvalidOperationException("not exercised here"),
            () => throw new InvalidOperationException("not exercised here"),
            NullLogger<MainViewModel>.Instance,
            new DiagnosticLogOptions(Path.Combine(Path.GetTempPath(), "linclelink-feedback-logs")),
            new LogoCatalog(),
            _paths,
            Substitute.For<IExceptionReporter>());
    }

    [Fact]
    public async Task A_running_operation_is_named_says_what_it_does_and_is_indeterminate_until_it_can_count()
    {
        var vm = CreateViewModel();
        var started = new TaskCompletionSource<OperationContext>();
        var finish = new TaskCompletionSource();

        var running = vm.RunOperationAsync("Import legacy DBInfo", op =>
        {
            started.SetResult(op);
            return finish.Task;
        });
        var op = await started.Task;

        // From the first moment: a name and a moving bar, not "0%".
        vm.IsBusy.Should().BeTrue();
        vm.OperationName.Should().Be("Import legacy DBInfo");
        vm.OperationStatus.Should().Be("Import legacy DBInfo...");
        vm.IsProgressIndeterminate.Should().BeTrue();

        op.Status.Report("Reading DBInfo.xml...");
        vm.OperationStatus.Should().Be("Reading DBInfo.xml...");

        op.Percent.Report(40);
        vm.IsProgressIndeterminate.Should().BeFalse();

        // A phase with nothing left to count: a bar stuck at 100% looks hung.
        op.Percent.Report(100);
        vm.IsProgressIndeterminate.Should().BeTrue();

        finish.SetResult();
        await running;

        vm.IsBusy.Should().BeFalse();
        vm.OperationName.Should().BeEmpty();
        vm.OperationStatus.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_says_so_and_a_late_status_line_does_not_take_it_back()
    {
        var vm = CreateViewModel();
        var started = new TaskCompletionSource<OperationContext>();
        var finish = new TaskCompletionSource();
        var running = vm.RunOperationAsync("Deploy to folder", op =>
        {
            started.SetResult(op);
            return finish.Task;
        });
        var op = await started.Task;

        vm.CancelOperationCommand.Execute(null);
        op.Status.Report("Linked 000123.ifs");
        op.Log.Report("Deploying IIDX 32...");

        // Work that was already under way keeps reporting for a moment.
        vm.OperationStatus.Should().Be("Cancelling...");

        finish.SetResult();
        await running;
    }

    [Fact]
    public async Task Log_lines_show_as_status_too_so_operations_that_only_log_are_not_silent()
    {
        var vm = CreateViewModel();

        string? seen = null;
        await vm.RunOperationAsync("Deploy to folder", op =>
        {
            op.Log.Report("Deploying IIDX 32...");
            seen = vm.OperationStatus;
            return Task.CompletedTask;
        });

        seen.Should().Be("Deploying IIDX 32...");
    }

    [Fact]
    public void Percent_reports_are_thinned_out_before_they_reach_the_ui_thread()
    {
        // A deploy links 150k files in seconds. One dispatcher post per file would
        // keep the UI thread busy with nothing but progress updates.
        var delivered = new List<double>();
        var progress = ProgressBridge.CreatePercent(delivered.Add);

        for (var i = 0; i <= 150_000; i++)
        {
            progress.Report(i * 100d / 150_000);
        }

        delivered.Should().HaveCountLessThan(1_100);
        delivered.First().Should().Be(0);
        delivered.Last().Should().Be(100, "the end of the measurable part must always arrive");
        delivered.Should().BeInAscendingOrder();
    }

    [Fact]
    public void A_percent_that_goes_back_to_zero_for_a_new_phase_still_arrives()
    {
        var delivered = new List<double>();
        var progress = ProgressBridge.CreatePercent(delivered.Add);

        progress.Report(50);
        progress.Report(50.01);
        progress.Report(0);

        delivered.Should().Equal(50, 0);
    }
}
