using FluentAssertions;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
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
    private readonly IAppDialogHost _dialogHost = Substitute.For<IAppDialogHost>();
    private readonly IThemeManager _themeManager = Substitute.For<IThemeManager>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IAppPaths _paths = Substitute.For<IAppPaths>();
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();

    private MainViewModel CreateViewModel() => new(
        new InstanceService(_fs, _hasher, _store, Substitute.For<IHardLinker>(), _repository, _driveInfo, _dialogs),
        new LinkingService(_fs, _store, Substitute.For<IHardLinker>(), _repository, _dialogs),
        new UnusedFilesService(_store, _repository, _dialogs),
        new LegacyImporter(_repository),
        new TorrentService(Substitute.For<ITorrentSource>(), _repository, _store, Substitute.For<IHardLinker>(), _fs),
        _repository,
        new StatusService(_store, _repository, _driveInfo, _paths),
        _dialogs,
        _dialogHost,
        _themeManager,
        _settingsStore,
        () => new AddInstanceViewModel(new InstanceService(_fs, _hasher, _store, Substitute.For<IHardLinker>(), _repository, _driveInfo, _dialogs), _dialogs));

    private void StubStatus(long dbSize = 0, long free = 1)
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(dbSize);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(free);
        _paths.DataDirectory.Returns("C:\\data");
    }

    [Fact]
    public async Task InitializeAsync_populates_instances_and_status()
    {
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            Instance.Create("A", [new InstanceFile("f.bin", "", 10, "A".PadRight(32, 'A') + ".bin")], []),
            Instance.Create("B", [], []),
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
        vm.LogLines.Should().Contain("Instance list updated.");
    }

    [Fact]
    public async Task OpenAddInstance_hosts_dialog()
    {
        StubStatus();
        var vm = CreateViewModel();

        await vm.OpenAddInstanceCommand.ExecuteAsync(null);

        await _dialogHost.Received(1).ShowDialogAsync(Arg.Any<AddInstanceViewModel>());
    }

    [Fact]
    public async Task DeleteInstance_deletes_after_confirmation()
    {
        StubStatus();
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Instance.Create("A", [], [])]);
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
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Instance.Create("A", [], [])]);
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
        _settingsStore.Load().Returns(new AppSettings(false, "C:\\data"));
        AppSettings? saved = null;
        _settingsStore.When(x => x.Save(Arg.Any<AppSettings>()))
            .Do(callInfo => saved = callInfo.Arg<AppSettings>());

        var vm = CreateViewModel();

        vm.IsDarkTheme = true;
        _themeManager.Received(1).Apply(true);
        saved.Should().NotBeNull();
        saved!.IsDarkTheme.Should().BeTrue();
        saved.DataDirectory.Should().Be("C:\\data");

        vm.IsDarkTheme = false;
        _themeManager.Received(1).Apply(false);
    }

    [Fact]
    public void Torrent_input_edits_reset_piece_gates()
    {
        StubStatus();
        var vm = CreateViewModel();
        vm.SelectedInstance = new InstanceListEntry("X", 0, 0, "0 B");

        vm.TorrentFilePath = "x.torrent";
        vm.CheckFilesCommand.CanExecute(null).Should().BeTrue();
        vm.CanCheckPieces.Should().BeFalse();

        vm.CanCheckPieces = true;
        vm.TorrentDownloadPath = "C:\\dl";
        vm.CanLinkTorrent = true;

        vm.RelativePath = @"contents\data";

        vm.CanCheckPieces.Should().BeFalse();
        vm.CanLinkTorrent.Should().BeFalse();
    }
}
