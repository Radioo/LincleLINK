using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Logos;
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
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class MainViewModelTests
{
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IThemeManager _themeManager = Substitute.For<IThemeManager>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IAppPaths _paths = Substitute.For<IAppPaths>();
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
    private readonly ITaskbarProgress _taskbarProgress = Substitute.For<ITaskbarProgress>();

    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IGameVersionDetector _detector = Substitute.For<IGameVersionDetector>();
    private readonly LogoCatalog _logoCatalog = new();

    private MainViewModel CreateViewModel() => new(
        new InstanceService(_fs, _hasher, _store, Substitute.For<IHardLinker>(), _preflight, _repository, _driveInfo, _dialogs, _detector),
        new LinkingService(_fs, _store, Substitute.For<IHardLinker>(), _preflight, _repository, _dialogs),
        new UnusedFilesService(_store, _repository, _dialogs),
        new LegacyImporter(_repository),
        new TorrentService(Substitute.For<ITorrentSource>(), _repository, _store, Substitute.For<IHardLinker>(), _fs),
        _repository,
        new StatusService(_store, _repository, _driveInfo, _paths),
        _dialogs,
        _themeManager,
        _settingsStore,
        _taskbarProgress,
        _preflight,
        () => new AddInstanceViewModel(
            new InstanceService(_fs, _hasher, _store, Substitute.For<IHardLinker>(), _preflight, _repository, _driveInfo, _dialogs, _detector),
            _dialogs, _taskbarProgress, _fs, _preflight, _detector),
        _logoCatalog, _paths);

    private void StubStatus(long dbSize = 0, long free = 1)
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(dbSize);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(free);
        _paths.DataDirectory.Returns("C:\\data");
    }

    [Fact]
    public async Task InitializeAsync_populates_instances_and_status()
    {
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([
            new InstanceListEntry("A", 1, 10, "10 B"),
            new InstanceListEntry("B", 0, 0, "0 B"),
        ]);
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(10L);
        _driveInfo.GetAvailableFreeSpace("C:\\data").Returns(500L);
        _paths.DataDirectory.Returns("C:\\data");

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        vm.Instances.Should().HaveCount(2);
        vm.Instances[0].InstanceName.Should().Be("A");
        vm.DbSize.Should().Be("10 B");
        vm.Savings.Should().Be("0 B"); // (10 + 0) - 10
        vm.FreeSpace.Should().Be("500 B");
        vm.LogLines.Should().Contain(LogMessages.LibraryRefreshed);
    }

    [Fact]
    public async Task RefreshInstances_orders_by_supported_list_not_by_name()
    {
        StubStatus();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([
            Entry("SDVX EXCEED GEAR", "SDVX/SDVX_EXCEED_GEAR_logo"),
            Entry("beatmania IIDX 9th style", "IIDX/AC_9th_style_logo"),
            Entry("SDVX NABLA", "SDVX/SDVX_NABLA_logo"),
            Entry("SDVX BOOTH", "SDVX/SDVX_BOOTH_logo"),
            Entry("ZZZ unknown", null),
        ]);

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        vm.Instances.Select(i => i.InstanceName).Should().Equal(
            "beatmania IIDX 9th style", // catalog index 0
            "SDVX BOOTH",               // first SDVX entry
            "SDVX EXCEED GEAR",         // second-to-last SDVX entry
            "SDVX NABLA",               // last SDVX entry
            "ZZZ unknown");             // no known logo sorts after, by name
    }

    private static InstanceListEntry Entry(string name, string? logoKey) =>
        new(name, 1, 10, "10 B")
        {
            DetectedGame = logoKey is null ? null : new GameVersionInfo(
                "KFC", "SOUND VOLTEX", null, null, name, logoKey, DetectionConfidence.Xml),
        };

    [Fact]
    public void OpenAddInstance_opens_panel_and_forwards_thread_count()
    {
        StubStatus();
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
        var vm = CreateViewModel();

        vm.ThreadCount = 3;
        vm.OpenAddInstanceCommand.Execute(null);

        vm.IsAddPanelOpen.Should().BeTrue();
        vm.AddInstance.Should().NotBeNull();
        vm.AddInstance!.ThreadCount.Should().Be(3);
    }

    [Fact]
    public void OpenAddInstance_is_idempotent_while_panel_is_open()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.OpenAddInstanceCommand.Execute(null);
        var first = vm.AddInstance;
        vm.OpenAddInstanceCommand.Execute(null);

        vm.AddInstance.Should().BeSameAs(first);
    }

    [Fact]
    public void Closing_the_add_panel_clears_it_and_refreshes()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.OpenAddInstanceCommand.Execute(null);
        var panel = vm.AddInstance!;
        panel.CloseCommand.Execute(null);

        vm.IsAddPanelOpen.Should().BeFalse();
        vm.AddInstance.Should().BeNull();
        vm.OpenAddInstanceCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Closing_the_add_panel_survives_failing_refresh()
    {
        StubStatus();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<InstanceListEntry>>(new IOException("db unavailable")));
        var vm = CreateViewModel();

        vm.OpenAddInstanceCommand.Execute(null);
        vm.AddInstance!.CloseCommand.Execute(null);

        // The refresh runs fire-and-forget; give the failed task a beat to land.
        await Task.Delay(50, TestContext.Current.CancellationToken);

        vm.IsAddPanelOpen.Should().BeFalse();
        vm.LogLines.Should().Contain(m => m.Contains("Could not refresh the library"));
    }

    [Fact]
    public void Filter_narrows_the_grid_and_clears_hidden_selection()
    {
        StubStatus();
        var vm = CreateViewModel();
        vm.Instances.Add(new InstanceListEntry("IIDX 31", 1, 10, "10 B"));
        vm.Instances.Add(new InstanceListEntry("SDVX VI", 1, 10, "10 B"));
        vm.FilterText = " "; // whitespace = no filter, but triggers ApplyFilter over the seeded list

        vm.FilteredInstances.Should().HaveCount(2);

        vm.SelectedInstance = vm.FilteredInstances[1];
        vm.FilterText = "iidx";

        vm.FilteredInstances.Should().ContainSingle(e => e.InstanceName == "IIDX 31");
        vm.SelectedInstance.Should().BeNull();
    }

    [Fact]
    public void Nav_index_drives_page_flags()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.IsLibraryPage.Should().BeTrue();

        vm.SelectedNavIndex = 1;
        vm.IsLibraryPage.Should().BeFalse();
        vm.IsTorrentPage.Should().BeTrue();

        vm.SelectedNavIndex = 2;
        vm.IsSettingsPage.Should().BeTrue();
        vm.IsTorrentPage.Should().BeFalse();
    }

    [Fact]
    public void ReportOutcome_sets_activity_line_and_warning_flag()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.ReportOutcome("✓ Deployed 10 files");
        vm.LastOutcome.Should().Be("✓ Deployed 10 files");
        vm.LastOutcomeIsWarning.Should().BeFalse();

        vm.ReportOutcome("⚠ 3 failed", isWarning: true);
        vm.LastOutcomeIsWarning.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteInstance_deletes_after_confirmation()
    {
        StubStatus();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([new InstanceListEntry("A", 0, 0, "0 B")]);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _repository.DeleteAsync("A", Arg.Any<CancellationToken>()).Returns(true);

        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.DeleteInstanceCommand.ExecuteAsync(null);

        await _repository.Received(1).DeleteAsync("A", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteInstance_cancelled_does_not_delete()
    {
        StubStatus();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([new InstanceListEntry("A", 0, 0, "0 B")]);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.DeleteInstanceCommand.ExecuteAsync(null);

        await _repository.DidNotReceive().DeleteAsync("A", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DeleteInstance_command_is_gated_on_selection()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.DeleteInstanceCommand.CanExecute(null).Should().BeFalse();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        vm.DeleteInstanceCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Link_and_copy_commands_are_gated_on_selection_and_busy()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.LinkFilesCommand.CanExecute(null).Should().BeFalse();
        vm.CopyHashedCommand.CanExecute(null).Should().BeFalse();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        vm.LinkFilesCommand.CanExecute(null).Should().BeTrue();
        vm.CopyHashedCommand.CanExecute(null).Should().BeTrue();

        vm.IsBusy = true;
        vm.LinkFilesCommand.CanExecute(null).Should().BeFalse();
        vm.CopyHashedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void IsDarkTheme_applies_theme_and_persists()
    {
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
        AppSettings? saved = null;
        _settingsStore.When(x => x.Save(Arg.Any<AppSettings>()))
            .Do(callInfo => saved = callInfo.Arg<AppSettings>());

        var vm = CreateViewModel();

        vm.IsDarkTheme = true;
        _themeManager.Received(1).Apply(AppTheme.Dark);
        saved.Should().NotBeNull();
        saved!.Theme.Should().Be(AppTheme.Dark);
        saved.DataDirectory.Should().Be("C:\\data");
        saved.HashThreadCount.Should().Be(2);
        vm.IsLightTheme.Should().BeFalse();
        vm.IsSystemTheme.Should().BeFalse();

        vm.IsLightTheme = true;
        _themeManager.Received(1).Apply(AppTheme.Light);
        vm.IsDarkTheme.Should().BeFalse();
    }

    [Fact]
    public void IsLightTheme_turns_dark_off_and_persists()
    {
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Dark, "C:\\data", 2));
        var vm = CreateViewModel();
        vm.IsDarkTheme = true;

        vm.IsLightTheme = true;

        vm.IsDarkTheme.Should().BeFalse();
        _themeManager.Received(1).Apply(AppTheme.Light);
    }

    [Fact]
    public void IsSystemTheme_applies_system_theme_and_persists()
    {
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
        AppSettings? saved = null;
        _settingsStore.When(x => x.Save(Arg.Any<AppSettings>()))
            .Do(callInfo => saved = callInfo.Arg<AppSettings>());

        var vm = CreateViewModel();

        vm.IsSystemTheme = true;

        _themeManager.Received(1).Apply(AppTheme.System);
        saved.Should().NotBeNull();
        saved!.Theme.Should().Be(AppTheme.System);
        vm.IsLightTheme.Should().BeFalse();
        vm.IsDarkTheme.Should().BeFalse();
        vm.Theme.Should().Be(AppTheme.System);
    }

    [Fact]
    public void ThreadCount_persists_and_clamps()
    {
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
        AppSettings? saved = null;
        _settingsStore.When(x => x.Save(Arg.Any<AppSettings>()))
            .Do(callInfo => saved = callInfo.Arg<AppSettings>());

        var vm = CreateViewModel();
        int threadCount = vm.MaxThreadCount;
        int requested = Math.Max(1, threadCount-1);
        vm.ThreadCount = requested;

        saved.Should().NotBeNull();
        saved!.HashThreadCount.Should().Be(requested);
        saved.Theme.Should().Be(AppTheme.Light);
        saved.DataDirectory.Should().Be("C:\\data");

        // Below the minimum clamps back to 1.
        vm.ThreadCount = 0;
        vm.ThreadCount.Should().Be(1);
        vm.MaxThreadCount.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public async Task ChangeDataDirectory_persists_and_flags_restart()
    {
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
        AppSettings? saved = null;
        _settingsStore.When(x => x.Save(Arg.Any<AppSettings>()))
            .Do(callInfo => saved = callInfo.Arg<AppSettings>());
        _dialogs.PickFolderAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("C:\\new-data");

        var vm = CreateViewModel();
        vm.DataDirectory = "C:\\data";

        await vm.ChangeDataDirectoryCommand.ExecuteAsync(null);

        // The picker must open in the currently configured directory.
        await _dialogs.Received(1).PickFolderAsync(Arg.Any<string>(), "C:\\data");
        saved.Should().NotBeNull();
        saved!.DataDirectory.Should().Be("C:\\new-data");
        saved.Theme.Should().Be(AppTheme.Light);
        saved.HashThreadCount.Should().Be(2);
        vm.DataDirectory.Should().Be("C:\\new-data");
        vm.DataDirectoryChangePending.Should().BeTrue();
        vm.LogLines.Should().Contain(m => m.Contains("Restart"));

        // The restart requirement must be explicit: a popup, not just a log line.
        await _dialogs.Received(1).InfoAsync(
            Arg.Is<string>(m => m != null && m.Contains("Restart")),
            "Restart required");
    }

    [Fact]
    public async Task ChangeDataDirectory_cancelled_shows_no_restart_popup()
    {
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
        _dialogs.PickFolderAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns((string?)null);

        var vm = CreateViewModel();
        vm.DataDirectory = "C:\\data";

        await vm.ChangeDataDirectoryCommand.ExecuteAsync(null);

        await _dialogs.DidNotReceiveWithAnyArgs().InfoAsync(default!, default!);
    }

    [Fact]
    public async Task ChangeDataDirectory_cancelled_or_unchanged_saves_nothing()
    {
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
        _dialogs.PickFolderAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns((string?)null);

        var vm = CreateViewModel();
        vm.DataDirectory = "C:\\data";

        await vm.ChangeDataDirectoryCommand.ExecuteAsync(null);

        _settingsStore.DidNotReceive().Save(Arg.Any<AppSettings>());
        vm.DataDirectoryChangePending.Should().BeFalse();

        // Re-picking the current directory (any casing) is a no-op too.
        _dialogs.PickFolderAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns("c:\\DATA");

        await vm.ChangeDataDirectoryCommand.ExecuteAsync(null);

        _settingsStore.DidNotReceive().Save(Arg.Any<AppSettings>());
        vm.DataDirectoryChangePending.Should().BeFalse();
    }

    [Fact]
    public void ChangeDataDirectory_command_is_gated_on_busy()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.ChangeDataDirectoryCommand.CanExecute(null).Should().BeTrue();

        vm.IsBusy = true;
        vm.ChangeDataDirectoryCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Torrent_input_edits_reset_piece_gates()
    {
        StubStatus();
        var vm = CreateViewModel();
        vm.TorrentCheck.TorrentInstance = new InstanceListEntry("X", 0, 0, "0 B");

        vm.TorrentCheck.TorrentFilePath = "x.torrent";
        vm.TorrentCheck.CheckFilesCommand.CanExecute(null).Should().BeTrue();
        vm.TorrentCheck.FilesMatched.Should().BeFalse();

        vm.TorrentCheck.FilesMatched = true;
        vm.TorrentCheck.TorrentDownloadPath = "C:\\dl";
        vm.TorrentCheck.PiecesVerified = true;

        vm.TorrentCheck.RelativePath = @"contents\data";

        vm.TorrentCheck.FilesMatched.Should().BeFalse();
        vm.TorrentCheck.PiecesVerified.Should().BeFalse();
    }

    [Fact]
    public void Torrent_check_files_gated_on_selection_and_busy()
    {
        StubStatus();
        var vm = CreateViewModel();

        // No torrent instance selected: not executable.
        vm.TorrentCheck.TorrentFilePath = "x.torrent";
        vm.TorrentCheck.CheckFilesCommand.CanExecute(null).Should().BeFalse();

        vm.TorrentCheck.TorrentInstance = new InstanceListEntry("X", 0, 0, "0 B");
        vm.TorrentCheck.CheckFilesCommand.CanExecute(null).Should().BeTrue();

        // Busy gates it again through the shared host.
        vm.IsBusy = true;
        vm.TorrentCheck.CheckFilesCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Torrent_command_requery_raised_on_selection_and_busy_changes()
    {
        StubStatus();
        var vm = CreateViewModel();

        var checkFilesFired = 0;
        var checkPiecesFired = 0;
        var linkFired = 0;
        var browseFileFired = 0;
        var browseDlFired = 0;
        vm.TorrentCheck.CheckFilesCommand.CanExecuteChanged += (_, _) => checkFilesFired++;
        vm.TorrentCheck.CheckPiecesCommand.CanExecuteChanged += (_, _) => checkPiecesFired++;
        vm.TorrentCheck.LinkToTorrentCommand.CanExecuteChanged += (_, _) => linkFired++;
        vm.TorrentCheck.BrowseTorrentFileCommand.CanExecuteChanged += (_, _) => browseFileFired++;
        vm.TorrentCheck.BrowseTorrentDlPathCommand.CanExecuteChanged += (_, _) => browseDlFired++;

        // Only CheckFiles gates on the torrent instance, so only it must re-query.
        vm.TorrentCheck.TorrentInstance = new InstanceListEntry("X", 0, 0, "0 B");
        checkFilesFired.Should().Be(1);
        checkPiecesFired.Should().Be(0);
        linkFired.Should().Be(0);
        browseFileFired.Should().Be(0);
        browseDlFired.Should().Be(0);

        // Every torrent command gates on IsBusy, so all must re-query.
        vm.IsBusy = true;
        checkFilesFired.Should().Be(2);
        checkPiecesFired.Should().Be(1);
        linkFired.Should().Be(1);
        browseFileFired.Should().Be(1);
        browseDlFired.Should().Be(1);
    }

    [Fact]
    public void Main_instance_selection_does_not_affect_torrent_instance()
    {
        StubStatus();
        var vm = CreateViewModel();

        vm.TorrentCheck.TorrentInstance = new InstanceListEntry("A", 0, 0, "0 B");
        vm.TorrentCheck.TorrentFilePath = "x.torrent";
        vm.SelectedInstance = new InstanceListEntry("B", 0, 0, "0 B");

        vm.TorrentCheck.TorrentInstance!.InstanceName.Should().Be("A");
        vm.TorrentCheck.CheckFilesCommand.CanExecute(null).Should().BeTrue();
    }
}
