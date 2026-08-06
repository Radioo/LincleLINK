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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// Remaining <see cref="MainViewModel"/> branches: the logo picker flow, operation
/// error/cancel handling (now surfaced via dialogs and the logger), and refresh
/// failure paths.
/// </summary>
public sealed class MainViewModelCoverageTests : IDisposable
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
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IGameVersionDetector _detector = Substitute.For<IGameVersionDetector>();
    private readonly LogoCatalog _logoCatalog = new();
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private MainViewModel CreateViewModel(ILogger<MainViewModel>? logger = null) => new(
        new InstanceService(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, _detector, NullLogger<InstanceService>.Instance),
        new LinkingService(_fs, _store, _hardLinker, _preflight, _repository, _dialogs, NullLogger<LinkingService>.Instance),
        new UnusedFilesService(_store, _repository, _dialogs, NullLogger<UnusedFilesService>.Instance),
        new LegacyImporter(_repository, NullLogger<LegacyImporter>.Instance),
        new TorrentService(Substitute.For<ITorrentSource>(), _repository, _store, _hardLinker, _fs, NullLogger<TorrentService>.Instance),
        _repository,
        new StatusService(_store, _repository, _driveInfo, _paths, NullLogger<StatusService>.Instance),
        _dialogs,
        _themeManager,
        _settingsStore,
        _taskbarProgress,
        _preflight,
            () => new AddInstanceViewModel(
            new InstanceService(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, _detector, NullLogger<InstanceService>.Instance),
            _dialogs, _taskbarProgress, _fs, _preflight, NullLogger<AddInstanceViewModel>.Instance, _detector),
        logger ?? NullLogger<MainViewModel>.Instance,
        new DiagnosticLogOptions(Path.Combine(_temp.Root, "logs")),
        _logoCatalog, _paths);
    private void StubEmptyLibrary()
    {
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(0L);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1L);
        _paths.DataDirectory.Returns("C:\\data");
        _settingsStore.Load().Returns(new AppSettings(AppTheme.Light, "C:\\data", 2));
    }

    private static InstanceListEntry Entry(string name = "X") => new(name, 0, 0, "0 B");

    [Fact]
    public void View_mode_toggle_flips_flag()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();

        vm.ToggleViewModeCommand.Execute(null);
        vm.IsGridView.Should().BeTrue();
        vm.ToggleViewModeCommand.Execute(null);
        vm.IsGridView.Should().BeFalse();
    }

    [Fact]
    public void Grid_view_change_persists_view_mode()
    {
        StubEmptyLibrary();
        AppSettings? saved = null;
        _settingsStore.When(x => x.Save(Arg.Any<AppSettings>())).Do(ci => saved = ci.Arg<AppSettings>());
        var vm = CreateViewModel();

        vm.IsGridView = true;

        saved.Should().NotBeNull();
        saved!.ViewMode.Should().Be(LibraryViewMode.Grid);
    }

    [Fact]
    public void Logo_picker_opens_with_all_logos_and_closes()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();

        vm.OpenLogoPickerCommand.Execute(null);

        vm.IsLogoPickerOpen.Should().BeTrue();
        vm.AvailableLogos.Should().HaveCount(_logoCatalog.AllLogos.Count);

        vm.CloseLogoPickerCommand.Execute(null);
        vm.IsLogoPickerOpen.Should().BeFalse();
    }

    [Fact]
    public async Task SetCustomLogo_without_selection_is_a_noop()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();

        await vm.SetCustomLogoCommand.ExecuteAsync(new LogoEntry("k", "avares://x.png", "x"));

        await _repository.DidNotReceiveWithAnyArgs().SetCustomLogoAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCustomLogo_resets_to_auto_when_null()
    {
        StubEmptyLibrary();
        var dataDir = _temp.Root;
        _paths.DataDirectory.Returns(dataDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "custom_logos"));
        var customFile = Path.Combine(dataDir, "custom_logos", "x.png");
        File.WriteAllText(customFile, "png");
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _repository.GetUniqueSizeAsync("X", Arg.Any<CancellationToken>()).Returns(0L);
        var vm = CreateViewModel();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        await vm.SetCustomLogoCommand.ExecuteAsync(null);

        await _repository.Received(1).SetCustomLogoAsync("X", null, Arg.Any<CancellationToken>());
        File.Exists(customFile).Should().BeFalse();
    }

    [Fact]
    public async Task SetCustomLogo_applies_the_picked_logo()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        await vm.SetCustomLogoCommand.ExecuteAsync(new LogoEntry("IIDX/AC_Lincle_logo", "avares://LincleLINK/Assets/IIDX/AC_Lincle_logo.png", "Lincle"));

        await _repository.Received(1).SetCustomLogoAsync("X", "IIDX/AC_Lincle_logo", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCustomImage_picks_a_file_and_saves_a_custom_logo()
    {
        StubEmptyLibrary();
        var picked = Path.Combine(_temp.Root, "custom.png");
        Directory.CreateDirectory(Path.GetDirectoryName(picked)!);
        File.WriteAllText(picked, "png");
        _dialogs.PickOpenFileAsync(Arg.Any<string>(), Arg.Any<FileType>()).Returns(picked);
        var dataDir = _temp.Root;
        _paths.DataDirectory.Returns(dataDir);
        var vm = CreateViewModel();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        await vm.SetCustomImageCommand.ExecuteAsync(null);

        await _repository.Received(1).SetCustomLogoAsync("X", "custom", Arg.Any<CancellationToken>());
        File.Exists(Path.Combine(dataDir, "custom_logos", "x.png")).Should().BeTrue();
    }

    [Fact]
    public async Task SetCustomLogo_surfaces_repository_failure_without_throwing()
    {
        StubEmptyLibrary();
        _repository.SetCustomLogoAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new IOException("locked"));
        var vm = CreateViewModel();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        var act = async () => await vm.SetCustomLogoCommand.ExecuteAsync(new LogoEntry("k", "avares://x.png", "x"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetCustomImage_surfaces_copy_failure_without_throwing()
    {
        StubEmptyLibrary();
        var missingPicked = Path.Combine(_temp.Root, "missing.png");
        _dialogs.PickOpenFileAsync(Arg.Any<string>(), Arg.Any<FileType>()).Returns(missingPicked);
        var dataDir = _temp.Root;
        _paths.DataDirectory.Returns(dataDir);
        var vm = CreateViewModel();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        var act = async () => await vm.SetCustomImageCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteInstance_removes_the_custom_logo()
    {
        StubEmptyLibrary();
        var dataDir = _temp.Root;
        _paths.DataDirectory.Returns(dataDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "custom_logos"));
        var customFile = Path.Combine(dataDir, "custom_logos", "x.png");
        File.WriteAllText(customFile, "png");
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _repository.DeleteAsync("X", Arg.Any<CancellationToken>()).Returns(true);
        var vm = CreateViewModel();

        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");
        await vm.DeleteInstanceCommand.ExecuteAsync(null);

        File.Exists(customFile).Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_is_idempotent()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        _repository.ClearReceivedCalls();
        await vm.InitializeAsync();

        await _repository.DidNotReceive().GetSummariesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Link_files_cancelled_logs_deploy_cancelled()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns((string?)null);
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(LoggerFactory.Create(b => b.AddProvider(provider)).CreateLogger<MainViewModel>());
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.LinkFilesCommand.ExecuteAsync(null);

        provider.Logs.Should().Contain(l => l.Message.Contains("cancelled"));
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Link_files_error_shows_dialog()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("X", Arg.Any<CancellationToken>()).Returns((Instance?)null);
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.LinkFilesCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ErrorAsync(
            Arg.Is<string>(m => m != null && m.Contains("not found")), "Deploy to folder");
    }

    [Fact]
    public async Task Link_files_partial_failure_shows_dialog()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        var instance = Instance.Create("X", [new InstanceFile("a.bin", "", 1, "A".PadRight(32, 'A') + ".bin")], [""]);
        _repository.GetAsync("X", Arg.Any<CancellationToken>()).Returns(instance);
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = "boom";
            return false;
        });
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.LinkFilesCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ErrorAsync(
            Arg.Is<string>(m => m != null && m.Contains("1 failed")), "Deploy to folder");
    }

    [Fact]
    public async Task Copy_hashed_cancelled_logs_export_cancelled()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns((string?)null);
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(LoggerFactory.Create(b => b.AddProvider(provider)).CreateLogger<MainViewModel>());
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.CopyHashedCommand.ExecuteAsync(null);

        provider.Logs.Should().Contain(l => l.Message.Contains("cancelled"));
    }

    [Fact]
    public async Task Copy_hashed_success_completes_without_error_dialog()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\dest");
        var instance = Instance.Create("X", [new InstanceFile("a.bin", "", 1, "A".PadRight(32, 'A') + ".bin")], [""]);
        _repository.GetAsync("X", Arg.Any<CancellationToken>()).Returns(instance);
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.CopyHashedCommand.ExecuteAsync(null);

        await _dialogs.DidNotReceiveWithAnyArgs().ErrorAsync(default!, default!);
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task CheckUnused_clean_shows_info_dialog()
    {
        StubEmptyLibrary();
        _store.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repository.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var vm = CreateViewModel();

        await vm.CheckUnusedCommand.ExecuteAsync(null);

        await _dialogs.Received(1).InfoAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CheckUnused_deleted_files_are_removed()
    {
        StubEmptyLibrary();
        _store.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>())
            .Returns(["A".PadRight(32, 'A') + ".bin", "B".PadRight(32, 'B') + ".bin"]);
        _store.GetSize(Arg.Any<string>()).Returns(150L);
        _repository.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var vm = CreateViewModel();

        await vm.CheckUnusedCommand.ExecuteAsync(null);

        await _store.Received(2).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportLegacy_cancelled_logs_import_cancelled()
    {
        StubEmptyLibrary();
        _dialogs.PickOpenFileAsync(Arg.Any<string>(), Arg.Any<FileType>()).Returns((string?)null);
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(LoggerFactory.Create(b => b.AddProvider(provider)).CreateLogger<MainViewModel>());

        await vm.ImportLegacyCommand.ExecuteAsync(null);

        provider.Logs.Should().Contain(l => l.Message.Contains("Legacy import cancelled"));
    }

    [Fact]
    public async Task ImportLegacy_logs_imported_and_skipped_names()
    {
        StubEmptyLibrary();
        var xml = Path.Combine(_temp.Root, "DBInfo.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(xml)!);
        File.WriteAllText(xml, """
            <?xml version="1.0"?>
            <DBInfo>
              <InstanceList>
                <DataInstance>
                  <InstanceName>A</InstanceName>
                  <InstanceFiles />
                </DataInstance>
              </InstanceList>
            </DBInfo>
            """);
        _dialogs.PickOpenFileAsync(Arg.Any<string>(), Arg.Any<FileType>()).Returns(xml);
        _repository.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(
            LoggerFactory.Create(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Debug)).CreateLogger<MainViewModel>());

        await vm.ImportLegacyCommand.ExecuteAsync(null);

        provider.Logs.Should().Contain(l => l.Message == "Activity: Imported A into the library.");
        provider.Logs.Should().Contain(l => l.Message == "Activity: Import finished.");
    }

    [Fact]
    public async Task RunOperation_catches_cancellation_and_failures()
    {
        StubEmptyLibrary();
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(LoggerFactory.Create(b => b.AddProvider(provider)).CreateLogger<MainViewModel>());

        await vm.RunOperationAsync("Test op", _ => throw new OperationCanceledException());
        vm.IsBusy.Should().BeFalse();
        provider.Logs.Should().Contain(l => l.Message.Contains("cancelled"));

        await vm.RunOperationAsync("Test op", _ => throw new IOException("boom"));
        await _dialogs.Received(1).ErrorAsync("boom", "Operation failed");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task RunOperation_calls_taskbar_lifecycle()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();

        await vm.RunOperationAsync("Test op", _ => Task.CompletedTask);

        _taskbarProgress.Received(1).BeginOperation();
        _taskbarProgress.Received(1).EndOperation();
    }

    [Fact]
    public void Command_gates_reflect_busy_state()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();

        vm.CheckUnusedCommand.CanExecute(null).Should().BeTrue();
        vm.ImportLegacyCommand.CanExecute(null).Should().BeTrue();
        vm.CancelOperationCommand.CanExecute(null).Should().BeFalse();

        vm.IsBusy = true;
        vm.CheckUnusedCommand.CanExecute(null).Should().BeFalse();
        vm.ImportLegacyCommand.CanExecute(null).Should().BeFalse();

        vm.IsBusy = false;
        vm.CheckUnusedCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_status_failure_logs_gracefully()
    {
        StubEmptyLibrary();
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long>(new IOException("unplugged")));
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(LoggerFactory.Create(b => b.AddProvider(provider)).CreateLogger<MainViewModel>());

        await vm.RefreshStatusAsync();

        provider.Logs.Should().Contain(l => l.Message.Contains("Could not refresh status"));
    }

    [Fact]
    public async Task Selection_change_loads_unique_size_and_failure_shows_dash()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _repository.GetUniqueSizeAsync("X", Arg.Any<CancellationToken>()).Returns(42L);
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        vm.SelectedInstance = vm.Instances[0];
        await AsyncWaits.AwaitUntilAsync(() => vm.SelectedUniqueSizeText == "42 B");

        vm.SelectedUniqueSizeText.Should().Be("42 B");
    }

    [Fact]
    public async Task Effective_logo_and_resolution_for_custom_entries()
    {
        var dataDir = _temp.Root;
        Directory.CreateDirectory(Path.Combine(dataDir, "custom_logos"));
        File.WriteAllText(Path.Combine(dataDir, "custom_logos", "x.png"), "png");
        _paths.DataDirectory.Returns(dataDir);
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([
            new InstanceListEntry("X", 0, 0, "0 B") { CustomLogoSource = "custom" },
            new InstanceListEntry("Y", 0, 0, "0 B") { CustomLogoSource = "IIDX/AC_Lincle_logo" },
        ]);
        _repository.GetUniqueSizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(0L);
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var custom = vm.Instances.Single(i => i.InstanceName == "X");
        custom.LogoUri.Should().NotBeNull();

        var picked = vm.Instances.Single(i => i.InstanceName == "Y");
        picked.LogoUri.Should().Be(_logoCatalog.GetLogoPath("IIDX/AC_Lincle_logo"));
    }

    [Fact]
    public async Task Custom_logo_with_missing_file_resolves_to_null()
    {
        _paths.DataDirectory.Returns(_temp.Root);
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([
            new InstanceListEntry("X", 0, 0, "0 B") { CustomLogoSource = "custom" },
        ]);
        _repository.GetUniqueSizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(0L);
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        vm.Instances.Single(i => i.InstanceName == "X").LogoUri.Should().BeNull();
    }

    [Fact]
    public async Task Unique_size_failure_shows_a_dash()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _repository.GetUniqueSizeAsync("X", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long>(new IOException("db gone")));
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        vm.SelectedInstance = vm.Instances[0];
        await AsyncWaits.AwaitUntilAsync(() => vm.SelectedUniqueSizeText == "-");

        vm.SelectedUniqueSizeText.Should().Be("-");
    }

    [Fact]
    public void OnAddInstanceClosed_with_unexpected_sender_is_ignored()
    {
        StubEmptyLibrary();
        var vm = CreateViewModel();
        var handler = typeof(MainViewModel).GetMethod("OnAddInstanceClosed",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        handler.Invoke(vm, ["not a vm", EventArgs.Empty]);
    }

    [Fact]
    public async Task Successful_add_closes_the_panel()
    {
        StubEmptyLibrary();
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([Path.Combine(Data, "a.bin")]);
        _fs.EnumerateDirectories(Data, true).Returns([]);
        _fs.GetFileLength(Arg.Any<string>()).Returns(100);
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        _store.Exists(Arg.Any<string>()).Returns(false);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1_000_000_000_000L);
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var vm = CreateViewModel();

        vm.OpenAddInstanceCommand.Execute(null);
        var panel = vm.AddInstance!;
        panel.InstanceName = "inst";
        panel.DataPath = Data;
        panel.IsKeepChecked = true;
        await panel.CreateInstanceCommand.ExecuteAsync(null);

        vm.IsAddPanelOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Copy_hashed_error_shows_dialog()
    {
        StubEmptyLibrary();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([Entry("X")]);
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\dest");
        _repository.GetAsync("X", Arg.Any<CancellationToken>()).Returns((Instance?)null);
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.SelectedInstance = vm.Instances[0];

        await vm.CopyHashedCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ErrorAsync(
            Arg.Is<string>(m => m != null && m.Contains("not found")), "Export storage files");
    }

    [Fact]
    public async Task CheckUnused_cancelled_logs_cleanup_cancelled()
    {
        StubEmptyLibrary();
        _store.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns(["A".PadRight(32, 'A') + ".bin"]);
        _repository.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns([]);
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(LoggerFactory.Create(b => b.AddProvider(provider)).CreateLogger<MainViewModel>());

        await vm.CheckUnusedCommand.ExecuteAsync(null);

        provider.Logs.Should().Contain(l => l.Message.Contains("Storage cleanup cancelled"));
    }

    [Fact]
    public async Task ImportLegacy_logs_existing_entries_as_skipped()
    {
        StubEmptyLibrary();
        var xml = Path.Combine(_temp.Root, "DBInfo.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(xml)!);
        File.WriteAllText(xml, """
            <?xml version="1.0"?>
            <DBInfo>
              <InstanceList>
                <DataInstance>
                  <InstanceName>A</InstanceName>
                  <InstanceFiles />
                </DataInstance>
              </InstanceList>
            </DBInfo>
            """);
        _dialogs.PickOpenFileAsync(Arg.Any<string>(), Arg.Any<FileType>()).Returns(xml);
        _repository.ExistsAsync("A", Arg.Any<CancellationToken>()).Returns(true);
        using var provider = new RecordingLoggerProvider();
        var vm = CreateViewModel(
            LoggerFactory.Create(b => b.AddProvider(provider).SetMinimumLevel(LogLevel.Debug)).CreateLogger<MainViewModel>());

        await vm.ImportLegacyCommand.ExecuteAsync(null);

        provider.Logs.Should().Contain(l => l.Message == "Activity: A is already in the library. Not importing.");
    }

    [Fact]
    public async Task CancelOperation_requests_cancellation_of_the_running_operation()
    {
        StubEmptyLibrary();
        var gate = new TaskCompletionSource();
        var vm = CreateViewModel();
        CancellationToken? token = null;
        var run = vm.RunOperationAsync("Test op", op =>
        {
            token = op.CancellationToken;
            return gate.Task;
        });
        await AsyncWaits.AwaitUntilAsync(() => vm.CancelOperationCommand.CanExecute(null));

        vm.CancelOperationCommand.Execute(null);
        token.Should().NotBeNull();
        token!.Value.IsCancellationRequested.Should().BeTrue();

        gate.SetResult();
        await run;
    }

    private static string Data => Path.Combine(Path.GetTempPath(), "data");
}
