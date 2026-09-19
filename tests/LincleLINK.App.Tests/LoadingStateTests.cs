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
/// Every view begins in its loading state (CLAUDE.md). "Nothing here" is a result,
/// and a result exists only once a load has finished: an empty state that is bound
/// to "no items" alone also shows before the first load and flashes at startup.
/// </summary>
public sealed class LoadingStateTests
{
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IAppPaths _paths = Substitute.For<IAppPaths>();
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IGameVersionDetector _detector = Substitute.For<IGameVersionDetector>();

    private MainViewModel CreateShell()
    {
        _paths.DataDirectory.Returns(Path.Combine(Path.GetTempPath(), "linclelink-loading-tests"));
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(500L);
        return new MainViewModel(
            new InstanceService(_fs, _hasher, _store, Substitute.For<IHardLinker>(), _preflight, _repository, _driveInfo, _dialogs, _detector, NullLogger<InstanceService>.Instance),
            new LinkingService(_fs, _store, Substitute.For<IHardLinker>(), _preflight, _repository, _dialogs, NullLogger<LinkingService>.Instance),
            new UnusedFilesService(_store, _repository, _dialogs, NullLogger<UnusedFilesService>.Instance),
            new LegacyImporter(_repository, NullLogger<LegacyImporter>.Instance),
            new TorrentService(Substitute.For<ITorrentSource>(), _repository, _store, Substitute.For<IHardLinker>(), _fs, NullLogger<TorrentService>.Instance),
            _repository,
            new StatusService(_store, _repository, _driveInfo, _paths, NullLogger<StatusService>.Instance),
            _dialogs,
            Substitute.For<IThemeManager>(),
            _settingsStore,
            Substitute.For<ITaskbarProgress>(),
            _preflight,
            () => throw new InvalidOperationException("not exercised here"),
            () => throw new InvalidOperationException("not exercised here"),
            () => throw new InvalidOperationException("not exercised here"),
            NullLogger<MainViewModel>.Instance,
            new DiagnosticLogOptions(Path.Combine(Path.GetTempPath(), "linclelink-loading-logs")),
            new LogoCatalog(),
            _paths,
            Substitute.For<IExceptionReporter>());
    }

    // ── library ───────────────────────────────────────────────────────────

    [Fact]
    public void A_new_shell_is_loading_its_library_and_not_empty()
    {
        var vm = CreateShell();

        vm.IsLibraryLoading.Should().BeTrue();
        vm.IsLibraryEmpty.Should().BeFalse("nothing has been loaded yet, so nothing is known to be missing");
        vm.HasEntries.Should().BeFalse();
        vm.EntryCountText.Should().Be("Loading...");
        vm.EntryPickerPlaceholder.Should().Be("Loading entries...");
    }

    [Fact]
    public async Task The_empty_state_comes_only_after_a_load_that_found_nothing()
    {
        var reading = new TaskCompletionSource<IReadOnlyList<InstanceListEntry>>();
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns(reading.Task);
        var vm = CreateShell();
        var emptySeenWhileLoading = false;
        vm.PropertyChanged += (_, _) => emptySeenWhileLoading |= vm.IsLibraryLoading && vm.IsLibraryEmpty;

        var initializing = vm.InitializeAsync();

        vm.IsLibraryLoading.Should().BeTrue();
        vm.IsLibraryEmpty.Should().BeFalse();

        reading.SetResult([]);
        await initializing;

        vm.IsLibraryLoading.Should().BeFalse();
        vm.IsLibraryEmpty.Should().BeTrue();
        vm.EntryCountText.Should().Be("0 entries");
        vm.EntryPickerPlaceholder.Should().Be("No entries in the library yet");
        emptySeenWhileLoading.Should().BeFalse();
    }

    [Fact]
    public async Task A_load_that_found_entries_never_passes_through_the_empty_state()
    {
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>())
            .Returns([new InstanceListEntry("A", 1, 10, "10 B"), new InstanceListEntry("B", 2, 20, "20 B")]);
        var vm = CreateShell();
        var emptySeen = false;
        vm.PropertyChanged += (_, _) => emptySeen |= vm.IsLibraryEmpty;

        await vm.InitializeAsync();

        // A refresh rebuilds the list; passing through zero is not "empty" either.
        await vm.RefreshInstancesAsync();

        vm.HasEntries.Should().BeTrue();
        vm.IsLibraryLoading.Should().BeFalse("a refresh keeps what is on screen until the new list is there");
        vm.EntryCountText.Should().Be("2 entries");
        vm.EntryPickerPlaceholder.Should().Be("Select an entry");
        emptySeen.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_first_load_shows_the_reason_and_not_an_empty_library()
    {
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<InstanceListEntry>>(new IOException("database is locked")));
        var vm = CreateShell();

        var initializing = () => vm.InitializeAsync();

        await initializing.Should().ThrowAsync<IOException>();
        vm.IsLibraryLoading.Should().BeFalse("a spinner that never ends is no better than a wrong empty state");
        vm.LibraryLoadError.Should().Be("database is locked");
        vm.IsLibraryEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_is_its_own_state()
    {
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([new InstanceListEntry("IIDX 32", 1, 10, "10 B")]);
        var vm = CreateShell();
        await vm.InitializeAsync();

        vm.FilterText = "sdvx";

        vm.HasNoFilterMatches.Should().BeTrue();
        vm.IsLibraryEmpty.Should().BeFalse();

        vm.FilterText = "";
        vm.HasNoFilterMatches.Should().BeFalse();
    }

    // ── storage card ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_storage_card_says_it_is_measuring_until_the_first_figures_arrive()
    {
        var measuring = new TaskCompletionSource<long>();
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(measuring.Task);
        _repository.GetSummariesAsync(Arg.Any<CancellationToken>()).Returns([new InstanceListEntry("A", 1, 30, "30 B")]);
        var vm = CreateShell();

        vm.IsStatusLoading.Should().BeTrue();
        vm.SavingsHeadline.Should().Be("Measuring storage...");
        vm.DbSize.Should().Be("...");
        vm.LibrarySize.Should().Be("...");
        vm.FreeSpace.Should().Be("...");

        var refreshing = vm.RefreshStatusAsync();
        vm.IsStatusLoading.Should().BeTrue();

        measuring.SetResult(10);
        await refreshing;

        vm.IsStatusLoading.Should().BeFalse();
        vm.SavingsHeadline.Should().Be("Saving 20 B");
        vm.DbSize.Should().Be("10 B");
    }

    [Fact]
    public async Task A_storage_card_that_cannot_be_measured_stops_measuring_and_says_so()
    {
        _store.GetTotalSizeAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<long>(new IOException("drive not ready")));
        var vm = CreateShell();

        await vm.RefreshStatusAsync();

        vm.IsStatusLoading.Should().BeFalse();
        vm.SavingsHeadline.Should().Be("Storage could not be measured");
        vm.DbSize.Should().Be("-");
    }

    // ── files dialog ──────────────────────────────────────────────────────

    private InstanceFilesViewModel CreateFilesDialog()
        => new(
            _repository, _store, _dialogs,
            new InstanceUpdateService(_fs, _hasher, _store, _repository, _driveInfo, _paths, _dialogs, _detector, NullLogger<InstanceUpdateService>.Instance),
            Substitute.For<ITaskbarProgress>(), NullLogger<InstanceFilesViewModel>.Instance);

    [Fact]
    public void A_files_dialog_is_loading_from_the_moment_it_exists()
    {
        var vm = CreateFilesDialog();

        vm.IsLoading.Should().BeTrue("the shell shows the dialog before it starts the load");
        vm.IsEntryEmpty.Should().BeFalse();
        vm.Summary.Should().Be("Loading...");
    }

    [Fact]
    public async Task An_entry_without_files_says_so_once_it_has_loaded()
    {
        var reading = new TaskCompletionSource<Instance?>();
        _repository.GetAsync("empty", Arg.Any<CancellationToken>()).Returns(reading.Task);
        var vm = CreateFilesDialog();
        var loading = vm.LoadAsync("empty", TestContext.Current.CancellationToken);

        vm.IsEntryEmpty.Should().BeFalse();

        reading.SetResult(Instance.Create("empty", [], []));
        await loading;

        vm.IsLoading.Should().BeFalse();
        vm.IsEntryEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task A_tree_filter_that_matches_nothing_is_not_an_empty_entry()
    {
        _repository.GetAsync("A", Arg.Any<CancellationToken>())
            .Returns(Instance.Create("A", [new InstanceFile("a.bin", "", 1, "AA.bin")], []));
        var vm = CreateFilesDialog();
        await vm.LoadAsync("A", TestContext.Current.CancellationToken);

        vm.Tree.Filter = "zzz";

        vm.HasNoTreeMatches.Should().BeTrue();
        vm.IsEntryEmpty.Should().BeFalse();
    }

    // ── migration window ──────────────────────────────────────────────────

    [Fact]
    public void The_migration_bar_is_indeterminate_until_it_has_something_to_count()
    {
        var vm = new StorageMigrationViewModel(null!, NullLogger<StorageMigrationViewModel>.Instance);

        vm.IsProgressIndeterminate.Should().BeTrue();
        vm.Progress = 30;
        vm.IsProgressIndeterminate.Should().BeFalse();
    }
}
