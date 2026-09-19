using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels;
using LincleLINK.App.ViewModels.FileTree;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>Staging Pending changes in the files dialog and applying them (plan 16 D4 to D9).</summary>
public sealed class InstanceFilesStagingTests
{
    private static readonly DateTime Stamp = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly LincleLINK.Core.Abstractions.Games.IGameVersionDetector _detector =
        Substitute.For<LincleLINK.Core.Abstractions.Games.IGameVersionDetector>();
    private Instance? _saved;

    public InstanceFilesStagingTests()
    {
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(long.MaxValue);
        var instance = Instance.Create(
            "IIDX 32",
            [
                new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"),
                new InstanceFile("a.mp4", "movie", 5, "A.mp4"),
                new InstanceFile("readme.txt", "", 1, "R.txt"),
            ],
            ["modules", "movie"]);
        instance.DetectedGame = new GameVersionInfo(
            "LDJ", "beatmania IIDX", "2026031800", null, null, null, DetectionConfidence.Xml);
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(instance);
        _repository.SaveAsync(Arg.Do<Instance>(i => _saved = i), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // A dropped update pack: /drop/pack/modules/bm2dx.dll (changed) and /drop/pack/data/new.bin.
        Folder("/drop/pack", Dir("/drop/pack/modules"), Dir("/drop/pack/data"));
        Folder("/drop/pack/modules", OnDisk("/drop/pack/modules/bm2dx.dll", 120, "NEW"));
        Folder("/drop/pack/data", OnDisk("/drop/pack/data/new.bin", 7, "BIN"));
    }

    private void Folder(string path, params FileSystemEntry[] entries)
    {
        _fs.DirectoryExists(path).Returns(true);
        _fs.ListDirectory(path).Returns(entries);
    }

    private static FileSystemEntry Dir(string path) => new(path, Path.GetFileName(path), true, 0, Stamp, null);

    private FileSystemEntry OnDisk(string path, long length, string hash)
    {
        _fs.FileExists(path).Returns(true);
        _fs.GetFileLength(path).Returns(length);
        _fs.GetLastWriteTimeUtc(path).Returns(Stamp);
        _hasher.ComputeHashAsync(path, Arg.Any<CancellationToken>()).Returns(hash);
        return new FileSystemEntry(path, Path.GetFileName(path), false, length, Stamp, null);
    }

    private async Task<InstanceFilesViewModel> OpenAsync()
    {
        var paths = Substitute.For<IAppPaths>();
        paths.DbDirectory.Returns("/data/db");
        var service = new InstanceUpdateService(
            _fs, _hasher, _store, _repository, _driveInfo, paths, _dialogs, _detector, NullLogger<InstanceUpdateService>.Instance);
        var vm = new InstanceFilesViewModel(
            _repository, _store, _dialogs, service, Substitute.For<ITaskbarProgress>(),
            NullLogger<InstanceFilesViewModel>.Instance);
        await vm.LoadAsync("IIDX 32", TestContext.Current.CancellationToken);
        return vm;
    }

    /// <summary>
    /// A changed Source replans without anyone awaiting it. The planning flag is up
    /// by the time the setter returns, so waiting for it to drop is not a race.
    /// </summary>
    private static Task Settled(InstanceFilesViewModel vm)
        => TestHelpers.AsyncWaits.AwaitUntilAsync(() => !vm.IsPlanning && !vm.IsDetecting);

    /// <summary>Stands in for the UI thread: work that runs "on the caller" runs with this context current.</summary>
    private sealed class CallerContext : SynchronizationContext;

    [Fact]
    public async Task Opening_the_dialog_says_it_is_loading_and_reads_the_entry_off_the_callers_thread()
    {
        var reading = new TaskCompletionSource<Instance?>();
        bool? readOnCaller = null;
        _repository.GetAsync("slow", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            readOnCaller = SynchronizationContext.Current is CallerContext;
            return reading.Task;
        });
        var paths = Substitute.For<IAppPaths>();
        var repository = new LincleLINK.Core.Infrastructure.Instances.BackgroundInstanceRepository(_repository);
        var vm = new InstanceFilesViewModel(
            repository, _store, _dialogs,
            new InstanceUpdateService(_fs, _hasher, _store, repository, _driveInfo, paths, _dialogs, _detector, NullLogger<InstanceUpdateService>.Instance),
            Substitute.For<ITaskbarProgress>(), NullLogger<InstanceFilesViewModel>.Instance);

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CallerContext());
        Task loading;
        try
        {
            loading = vm.LoadAsync("slow", TestContext.Current.CancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => readOnCaller is not null);
        readOnCaller.Should().BeFalse("the UI never freezes: the database read may not run on the calling thread");
        vm.IsLoading.Should().BeTrue();
        vm.Activity.Should().Be("Loading the entry's files...");
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();

        reading.SetResult(Instance.Create("slow", [new InstanceFile("a.bin", "", 1, "AA.bin")], []));
        await loading;

        vm.IsLoading.Should().BeFalse();
        vm.HasActivity.Should().BeFalse();
        vm.Tree.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task A_staging_step_shows_that_the_preview_is_being_updated_and_holds_apply_until_it_is()
    {
        var vm = await OpenAsync();
        await vm.StageRemovalAsync([Row(vm, "readme.txt")]);

        // Looked at in the moment the flag goes up, which is on this thread and before
        // the thread pool gets the work. Looking after the call returns is a race: a
        // plan of three files can be done before the caller reaches its await, and the
        // call then returns with the flag already down again.
        string? activityWhilePlanning = null;
        bool? applyWhilePlanning = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstanceFilesViewModel.IsPlanning) && vm.IsPlanning)
            {
                activityWhilePlanning = vm.Activity;
                applyWhilePlanning = vm.ApplyCommand.CanExecute(null);
            }
        };

        var restaging = vm.StageRemovalAsync([Row(vm, "a.mp4")]);

        activityWhilePlanning.Should().Be("Updating the preview...", "the flag goes up before the call returns");
        applyWhilePlanning.Should().BeFalse("what is on screen is about to be replaced");

        await restaging;
        await Settled(vm);

        vm.HasActivity.Should().BeFalse();
        Row(vm, "a.mp4").IsRemoved.Should().BeTrue();
        await Settled(vm);
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Scanning_a_drop_shows_up_as_activity_before_any_source_row_exists()
    {
        using var gate = new ManualResetEventSlim();
        _fs.DirectoryExists("/drop/slow").Returns(true);
        _fs.ListDirectory("/drop/slow").Returns(_ =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return [];
        });
        var vm = await OpenAsync();

        var adding = vm.AddPathsAsync(["/drop/slow"]);

        vm.Sources.Should().BeEmpty();
        vm.Activity.Should().Be("Scanning the dropped files...");

        gate.Set();
        await adding;
        await Settled(vm);

        vm.HasActivity.Should().BeFalse();
    }

    [Fact]
    public async Task While_apply_has_nothing_to_count_its_bar_is_indeterminate()
    {
        var vm = await OpenAsync();

        vm.Progress = 0;
        vm.IsProgressIndeterminate.Should().BeTrue("checking the changes has no steps");
        vm.Progress = 40;
        vm.IsProgressIndeterminate.Should().BeFalse();
        vm.Progress = 100;
        vm.IsProgressIndeterminate.Should().BeTrue("the database save has no steps, and a bar stuck at 100% looks hung");
    }

    private static FileTreeRow Row(InstanceFilesViewModel vm, string name)
    {
        vm.Tree.ExpandAll();
        return vm.Tree.Rows.Single(r => r.Name == name);
    }

    [Fact]
    public async Task A_freshly_opened_dialog_has_nothing_to_apply()
    {
        var vm = await OpenAsync();

        vm.HasPendingChanges.Should().BeFalse();
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
        vm.ChangesSummary.Should().BeEmpty();
    }

    [Fact]
    public async Task A_staged_removal_shows_in_the_tree_and_summary_and_apply_saves_the_entry_without_it()
    {
        var vm = await OpenAsync();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.StageRemovalAsync([Row(vm, "movie")]);

        Row(vm, "a.mp4").IsRemoved.Should().BeTrue();
        vm.ChangesSummary.Should().Contain("1 file, 5 B will be removed from this entry");
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => !vm.IsDetecting);
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();

        await vm.ApplyCommand.ExecuteAsync(null);

        _saved!.FileList.Select(f => f.FileName).Should().BeEquivalentTo("bm2dx.dll", "readme.txt");
        _saved.DirectoryList.Should().Equal("modules");
        await _store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        vm.Applied.Should().BeTrue();
        closed.Should().BeTrue();
    }

    [Fact]
    public async Task Restore_takes_a_staged_removal_back()
    {
        var vm = await OpenAsync();
        await vm.StageRemovalAsync([Row(vm, "a.mp4")]);

        await vm.RestoreAsync([Row(vm, "a.mp4")]);

        Row(vm, "a.mp4").IsRemoved.Should().BeFalse();
        vm.HasPendingChanges.Should().BeFalse();
    }

    [Fact]
    public async Task A_file_that_could_not_be_hashed_leaves_the_sources_file_count()
    {
        _hasher.ComputeHashAsync("/drop/pack/data/new.bin", Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new IOException("The file is in use."));
        var vm = await OpenAsync();
        var raised = new System.Collections.Concurrent.ConcurrentBag<string?>();
        string? scanned = null;
        vm.Sources.CollectionChanged += (_, e) =>
        {
            foreach (StagedSourceViewModel added in e.NewItems ?? Array.Empty<object>())
            {
                scanned = added.Contents;
                added.PropertyChanged += (_, changed) => raised.Add(changed.PropertyName);
            }
        };

        await vm.AddPathsAsync(["/drop/pack"]);

        // The row says what Apply will bring in, not what the scan first saw, and
        // the binding has to hear about the change.
        var source = vm.Sources.Should().ContainSingle().Which;
        source.IsHashing.Should().BeFalse();
        source.HasIssues.Should().BeTrue();
        scanned.Should().Be("2 files, 127 B");
        source.Contents.Should().Be("1 files, 120 B");
        raised.Should().Contain(nameof(StagedSourceViewModel.Contents));
    }

    [Fact]
    public async Task A_dropped_folder_is_scanned_hashed_and_previewed_and_apply_copies_new_content()
    {
        var vm = await OpenAsync();

        // Hold the Storage lookup back, so the time before the figure is known is
        // always there and not only on a slow machine.
        using var storageAnswers = new ManualResetEventSlim();
        _store.Exists(Arg.Any<string>()).Returns(_ =>
        {
            storageAnswers.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            return false;
        });

        await vm.AddPathsAsync(["/drop/pack"]);

        var source = vm.Sources.Should().ContainSingle().Which;
        source.Label.Should().Be("pack");
        source.IsHashing.Should().BeFalse();
        Row(vm, "bm2dx.dll").Status.Should().Be(PlannedStatus.Replaced);
        Row(vm, "new.bin").Status.Should().Be(PlannedStatus.Added);

        // The figure arrives after a background Storage lookup. Until then the summary
        // says so, in words that include "new to storage", so waiting for those words
        // alone would not wait at all.
        vm.ChangesSummary.Should().Contain("1 added").And.Contain("1 replaced").And.Contain("Working out");
        storageAnswers.Set();
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.ChangesSummary.Contains("B new to storage"));
        vm.ChangesSummary.Should().Contain("1 added").And.Contain("1 replaced").And.Contain("127 B new to storage");
        vm.ChangesSummary.Should().NotContain("Working out");

        await vm.ApplyCommand.ExecuteAsync(null);

        await _store.Received(1).CopyToStoreAsync("/drop/pack/modules/bm2dx.dll", "NEW.dll", Arg.Any<CancellationToken>());
        await _store.Received(1).CopyToStoreAsync("/drop/pack/data/new.bin", "BIN.bin", Arg.Any<CancellationToken>());
        _saved!.FileList.Single(f => f.FileName == "bm2dx.dll").HashedFileName.Should().Be("NEW.dll");
        _saved.FileList.Should().HaveCount(4);
    }

    [Fact]
    public async Task The_changes_only_switch_says_how_many_changes_there_are_and_hides_the_rest()
    {
        var vm = await OpenAsync();
        vm.HasSources.Should().BeFalse();

        await vm.AddPathsAsync(["/drop/pack"]);
        await vm.StageRemovalAsync([Row(vm, "a.mp4")]);

        // 1 added, 1 replaced, 1 removed.
        vm.HasSources.Should().BeTrue();
        vm.ChangesOnlyLabel.Should().Be("Show only changes (3)");

        vm.Tree.ChangesOnly = true;
        vm.Tree.ExpandAll();

        vm.Tree.Rows.Where(r => !r.IsDirectory).Select(r => r.Name)
            .Should().BeEquivalentTo("bm2dx.dll", "new.bin", "a.mp4");
    }

    [Fact]
    public async Task Unticking_a_source_file_keeps_it_out_and_restore_brings_it_back()
    {
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"]);

        await vm.StageExclusionAsync([Row(vm, "bm2dx.dll")]);

        Row(vm, "bm2dx.dll").Status.Should().Be(PlannedStatus.Unchanged);
        Row(vm, "bm2dx.dll").IsExcluded.Should().BeTrue();

        await vm.RestoreAsync([Row(vm, "bm2dx.dll")]);

        Row(vm, "bm2dx.dll").Status.Should().Be(PlannedStatus.Replaced);
    }

    [Fact]
    public async Task Removing_a_source_takes_its_files_out_of_the_preview()
    {
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"]);

        vm.Sources[0].RemoveCommand.Execute(null);
        await Settled(vm);

        vm.Sources.Should().BeEmpty();
        vm.HasPendingChanges.Should().BeFalse();
        vm.Tree.Rows.Should().NotContain(r => r.Name == "data");
    }

    [Fact]
    public async Task A_source_can_target_a_folder_and_keep_its_own_folder()
    {
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"], destination: "movie");

        Row(vm, "new.bin").Path.Should().Be("movie/data/new.bin");

        vm.Sources[0].KeepTopFolder = true;
        await Settled(vm);

        Row(vm, "new.bin").Path.Should().Be("movie/pack/data/new.bin");

        vm.Sources[0].Destination = "";
        await Settled(vm);

        Row(vm, "new.bin").Path.Should().Be("pack/data/new.bin");
    }

    [Fact]
    public async Task A_collision_is_listed_and_blocks_apply()
    {
        Folder("/drop/bad", OnDisk("/drop/bad/modules", 1, "M"));
        var vm = await OpenAsync();

        await vm.AddPathsAsync(["/drop/bad"]);

        vm.Problems.Should().ContainSingle().Which.Should().Contain("modules");
        vm.ApplyCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>The entry holds no config of its own, so detection looks next to the dropped folder.</summary>
    private void DetectorFinds(string dateCode, string gameCode = "LDJ")
        => _detector.DetectAsync("/drop/pack", Arg.Any<CancellationToken>())
            .Returns(new DetectionResult(
                new GameVersionInfo(gameCode, "beatmania IIDX", dateCode, null, null, null, DetectionConfidence.Xml),
                null, null, false));

    [Fact]
    public async Task A_newly_detected_version_shows_in_the_preview_and_is_saved_with_apply()
    {
        DetectorFinds("2026091700");
        var vm = await OpenAsync();

        await vm.AddPathsAsync(["/drop/pack"]);
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.HasDetectedVersion);

        vm.DetectedVersionText.Should().Be("Detected version: 2026031800 -> 2026091700 (found next to the dropped folder)");
        vm.AdoptDetectedVersion.Should().BeTrue();

        await vm.ApplyCommand.ExecuteAsync(null);

        _saved!.DetectedGame!.DateCode.Should().Be("2026091700");
    }

    [Fact]
    public async Task Declining_the_detected_version_keeps_the_old_one()
    {
        DetectorFinds("2026091700");
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"]);
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.HasDetectedVersion);

        vm.AdoptDetectedVersion = false;
        await vm.ApplyCommand.ExecuteAsync(null);

        _saved!.DetectedGame!.DateCode.Should().Be("2026031800");
    }

    [Fact]
    public async Task Apply_waits_until_detection_has_settled()
    {
        var found = new TaskCompletionSource<DetectionResult>();
        _detector.DetectAsync("/drop/pack", Arg.Any<CancellationToken>()).Returns(found.Task);
        var vm = await OpenAsync();

        await vm.AddPathsAsync(["/drop/pack"]);
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.IsDetecting);

        vm.ApplyCommand.CanExecute(null).Should().BeFalse("the user has not seen what Apply would tag the entry with");

        found.SetResult(new DetectionResult(null, null, null, false));
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => !vm.IsDetecting);

        await Settled(vm);
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task A_tick_taken_off_one_version_does_not_carry_over_to_another()
    {
        DetectorFinds("2026091700");
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"]);
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.HasDetectedVersion);
        vm.AdoptDetectedVersion = false;

        // An unrelated staging step detects the same version again: the tick stays off.
        await vm.StageRemovalAsync([Row(vm, "a.mp4")]);
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.HasDetectedVersion);
        vm.AdoptDetectedVersion.Should().BeFalse();

        DetectorFinds("2026101500");
        await vm.RestoreAsync([Row(vm, "a.mp4")]);
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.DetectedVersionText.Contains("2026101500"));

        vm.AdoptDetectedVersion.Should().BeTrue();
    }

    [Fact]
    public async Task The_detected_version_line_goes_away_with_the_source_that_caused_it()
    {
        DetectorFinds("2026091700");
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"]);
        await TestHelpers.AsyncWaits.AwaitUntilAsync(() => vm.HasDetectedVersion);

        vm.Sources[0].RemoveCommand.Execute(null);

        vm.HasDetectedVersion.Should().BeFalse("the line belongs to the plan it was detected for");
        await Settled(vm);
        vm.HasDetectedVersion.Should().BeFalse();
    }

    [Fact]
    public async Task Removing_a_file_that_a_source_replaces_takes_the_path_out_altogether()
    {
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"]);

        await vm.StageRemovalAsync([Row(vm, "bm2dx.dll")]);

        Row(vm, "bm2dx.dll").IsRemoved.Should().BeTrue();

        await vm.RestoreAsync([Row(vm, "bm2dx.dll")]);

        Row(vm, "bm2dx.dll").Status.Should().Be(PlannedStatus.Replaced);
    }

    [Fact]
    public async Task A_source_that_only_brings_an_empty_folder_is_a_change_that_can_be_applied()
    {
        Folder("/drop/shell", Dir("/drop/shell/savedata"));
        Folder("/drop/shell/savedata");
        var vm = await OpenAsync();

        await vm.AddPathsAsync(["/drop/shell"]);

        Row(vm, "savedata").IsAdded.Should().BeTrue();
        vm.HasPendingChanges.Should().BeTrue();
        await Settled(vm);
        vm.ApplyCommand.CanExecute(null).Should().BeTrue();

        await vm.ApplyCommand.ExecuteAsync(null);

        _saved!.DirectoryList.Should().Contain("savedata");
    }

    [Fact]
    public async Task The_colliding_rows_are_flagged_in_the_tree()
    {
        Folder("/drop/bad", OnDisk("/drop/bad/modules", 1, "M"));
        var vm = await OpenAsync();

        await vm.AddPathsAsync(["/drop/bad"]);

        vm.Tree.Rows.Where(r => r.Name == "modules").Should().HaveCount(2).And.OnlyContain(r => r.IsCollision);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:/Windows")]
    public async Task A_destination_that_leaves_the_entry_is_refused(string destination)
    {
        var vm = await OpenAsync();
        await vm.AddPathsAsync(["/drop/pack"], destination: "movie");

        vm.Sources[0].Destination = destination;
        await Settled(vm);

        vm.Sources[0].Destination.Should().Be("movie");
        Row(vm, "new.bin").Path.Should().Be("movie/data/new.bin");
    }

    [Fact]
    public async Task A_scan_that_finishes_after_the_dialog_closed_stages_nothing()
    {
        using var gate = new ManualResetEventSlim();
        _fs.DirectoryExists("/drop/slow").Returns(true);
        _fs.ListDirectory("/drop/slow").Returns(_ =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return [];
        });
        var vm = await OpenAsync();

        var adding = vm.AddPathsAsync(["/drop/slow"]);
        await vm.CloseCommand.ExecuteAsync(null);
        gate.Set();
        await adding;

        vm.Sources.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Closing_with_pending_changes_asks_first(bool discard)
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), "Discard changes").Returns(discard);
        var vm = await OpenAsync();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        await vm.StageRemovalAsync([Row(vm, "a.mp4")]);

        await vm.CloseCommand.ExecuteAsync(null);

        closed.Should().Be(discard);
    }
}
