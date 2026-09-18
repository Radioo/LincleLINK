using FluentAssertions;
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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// <see cref="InstanceUpdateService"/> (plan 16 D5, D9): turning a drop into a
/// Source, hashing it, and applying Pending changes to an Instance.
/// </summary>
public sealed class InstanceUpdateServiceTests
{
    private static readonly DateTime Stamp = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IAppPaths _paths = Substitute.For<IAppPaths>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    public InstanceUpdateServiceTests()
    {
        _paths.DbDirectory.Returns("/data/db");
        _driveInfo.GetAvailableFreeSpace("/data/db").Returns(long.MaxValue);
    }

    private InstanceUpdateService CreateService() => new(
        _fs, _hasher, _store, _repository, _driveInfo, _paths, _dialogs,
        Substitute.For<LincleLINK.Core.Abstractions.Games.IGameVersionDetector>(), NullLogger<InstanceUpdateService>.Instance);

    private static FileSystemEntry Dir(string path, string? linkTarget = null)
        => new(path, Path.GetFileName(path), IsDirectory: true, 0, Stamp, linkTarget);

    private static FileSystemEntry FileEntry(string path, long length)
        => new(path, Path.GetFileName(path), IsDirectory: false, length, Stamp, null);

    private void Folder(string path, params FileSystemEntry[] entries)
    {
        _fs.DirectoryExists(path).Returns(true);
        _fs.ListDirectory(path).Returns(entries);
    }

    // ── scan ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task One_dropped_folder_becomes_a_source_whose_contents_are_relative_to_it()
    {
        Folder("/drop/2026091700", Dir("/drop/2026091700/data"), FileEntry("/drop/2026091700/readme.txt", 3));
        Folder("/drop/2026091700/data", Dir("/drop/2026091700/data/empty"), FileEntry("/drop/2026091700/data/x.bin", 10));
        Folder("/drop/2026091700/data/empty");

        var scan = await CreateService().ScanAsync(["/drop/2026091700"], TestContext.Current.CancellationToken);

        scan.Source.TopFolder.Should().Be("2026091700");
        scan.Source.Files.Should().BeEquivalentTo(
        [
            new SourceFile("", "readme.txt", 3, null) { FullPath = "/drop/2026091700/readme.txt", LastWriteTimeUtc = Stamp },
            new SourceFile("data", "x.bin", 10, null) { FullPath = "/drop/2026091700/data/x.bin", LastWriteTimeUtc = Stamp },
        ]);
        scan.Source.Directories.Should().BeEquivalentTo("data", "data/empty");
        scan.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Several_dropped_items_land_under_their_own_names()
    {
        Folder("/drop/modules", FileEntry("/drop/modules/bm2dx.dll", 5));
        _fs.FileExists("/drop/readme.txt").Returns(true);
        _fs.GetFileLength("/drop/readme.txt").Returns(3);
        _fs.GetLastWriteTimeUtc("/drop/readme.txt").Returns(Stamp);

        var scan = await CreateService().ScanAsync(["/drop/modules", "/drop/readme.txt"], TestContext.Current.CancellationToken);

        scan.Source.TopFolder.Should().BeNull();
        scan.Source.Files.Select(f => $"{f.RelativePath}|{f.FileName}").Should().BeEquivalentTo("modules|bm2dx.dll", "|readme.txt");
        scan.Source.Directories.Should().BeEquivalentTo("modules");
        scan.Source.DiskRoots.Should().Equal("/drop/modules");
    }

    [Fact]
    public async Task Linked_folders_are_skipped_and_unreadable_folders_reported_without_stopping_the_scan()
    {
        Folder("/drop/pack",
            Dir("/drop/pack/link", linkTarget: "/elsewhere"),
            Dir("/drop/pack/locked"),
            FileEntry("/drop/pack/ok.bin", 1));
        _fs.ListDirectory("/drop/pack/locked").Throws(new UnauthorizedAccessException("Access denied."));

        var scan = await CreateService().ScanAsync(["/drop/pack"], TestContext.Current.CancellationToken);

        scan.Source.Files.Should().ContainSingle().Which.FileName.Should().Be("ok.bin");
        scan.Source.Directories.Should().BeEmpty();
        scan.Issues.Should().BeEquivalentTo(
        [
            new SourceIssue("link", "Skipped, link to /elsewhere"),
            new SourceIssue("locked", "Access denied."),
        ]);
    }

    // ── hash ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hashing_names_each_file_by_hash_and_extension_and_drops_what_cannot_be_read()
    {
        var source = new UpdateSource(
            "",
            [
                new SourceFile("", "a.dll", 1, null) { FullPath = "/drop/a.dll" },
                new SourceFile("", "noext", 1, null) { FullPath = "/drop/noext" },
                new SourceFile("", "bad.bin", 1, null) { FullPath = "/drop/bad.bin" },
            ],
            []);
        _hasher.ComputeHashAsync("/drop/a.dll", Arg.Any<CancellationToken>()).Returns("AAAA");
        _hasher.ComputeHashAsync("/drop/noext", Arg.Any<CancellationToken>()).Returns("BBBB");
        _hasher.ComputeHashAsync("/drop/bad.bin", Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("Sharing violation."));

        var hashed = await CreateService().HashAsync(source, ct: TestContext.Current.CancellationToken);

        hashed.Source.Files.Select(f => f.HashedFileName).Should().Equal("AAAA.dll", "BBBB");
        hashed.Issues.Should().BeEquivalentTo([new SourceIssue("bad.bin", "Sharing violation.")]);
    }

    // ── apply ─────────────────────────────────────────────────────────────

    private static Instance Existing()
    {
        var instance = Instance.Create(
            "IIDX 32",
            [new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"), new InstanceFile("a.mp4", "movie", 5, "A.mp4")],
            ["modules", "movie"]);
        instance.DetectedGame = new GameVersionInfo("LDJ", "beatmania IIDX", "2026031800", null, null, null, DetectionConfidence.Xml);
        instance.CustomLogoSource = "IIDX/x";
        return instance;
    }

    private SourceFile OnDisk(string directory, string name, long size, string hash)
    {
        var path = $"/drop/{name}";
        _fs.FileExists(path).Returns(true);
        _fs.GetFileLength(path).Returns(size);
        _fs.GetLastWriteTimeUtc(path).Returns(Stamp);
        return new SourceFile(directory, name, size, hash) { FullPath = path, LastWriteTimeUtc = Stamp };
    }

    [Fact]
    public async Task Apply_copies_only_content_new_to_storage_then_saves_the_resulting_instance()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Existing());
        _store.Exists("KNOWN.bin").Returns(true);
        Instance? saved = null;
        await _repository.SaveAsync(Arg.Do<Instance>(i => saved = i), Arg.Any<CancellationToken>());
        var changes = new PendingChanges(
            [new UpdateSource("", [
                OnDisk("modules", "bm2dx.dll", 120, "NEW.dll"),
                OnDisk("data", "known.bin", 7, "KNOWN.bin"),
                OnDisk("data", "skipped.bin", 9, "SKIP.bin"),
            ], [])],
            ExcludedPaths: ["data/skipped.bin"],
            RemovedPaths: ["movie"]);

        var result = await CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(new ApplyUpdateResult(true, null, Added: 1, Replaced: 1, Removed: 1, BytesAddedToStorage: 120));
        await _store.Received(1).CopyToStoreAsync("/drop/bm2dx.dll", "NEW.dll", Arg.Any<CancellationToken>());
        await _store.Received(1).CopyToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        saved.Should().NotBeNull();
        saved!.InstanceName.Should().Be("IIDX 32");
        saved.FileList.Select(f => $"{PathNormalizer.Canonicalize(f.RelativePath)}/{f.FileName}={f.HashedFileName}")
            .Should().BeEquivalentTo("modules/bm2dx.dll=NEW.dll", "data/known.bin=KNOWN.bin");
        saved.DirectoryList.Select(PathNormalizer.Canonicalize).Should().BeEquivalentTo("modules", "data");
        saved.DetectedGame.Should().Be(Existing().DetectedGame);
        saved.CustomLogoSource.Should().Be("IIDX/x");
    }

    [Fact]
    public async Task A_failed_copy_leaves_the_instance_untouched()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Existing());
        _store.CopyToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Disk full."));
        var changes = new PendingChanges([new UpdateSource("", [OnDisk("", "new.bin", 1, "N.bin")], [])], [], []);

        var apply = () => CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);

        await apply.Should().ThrowAsync<IOException>();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_source_file_that_changed_since_it_was_hashed_refuses_the_apply()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Existing());
        var file = OnDisk("", "new.bin", 1, "N.bin");
        _fs.GetLastWriteTimeUtc(file.FullPath).Returns(Stamp.AddMinutes(5));
        var changes = new PendingChanges([new UpdateSource("", [file], [])], [], []);

        var result = await CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("new.bin").And.Contain("changed");
        await _store.DidNotReceive().CopyToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Collisions_and_unhashed_files_refuse_the_apply()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Existing());
        var changes = new PendingChanges([new UpdateSource("", [new SourceFile("", "modules", 1, "M.bin")], [])], [], []);

        var result = await CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("modules");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:/Windows")]
    public async Task A_destination_that_leaves_the_entry_refuses_the_apply(string destination)
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Existing());
        var changes = new PendingChanges([new UpdateSource(destination, [OnDisk("", "new.bin", 1, "N.bin")], [])], [], []);

        var result = await CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not a valid path");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_does_its_lookup_planning_and_saving_off_the_callers_thread_and_says_what_it_does()
    {
        var context = new RecordingContext();
        var onCaller = new List<string>();
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (SynchronizationContext.Current is RecordingContext) onCaller.Add("lookup");
            return Existing();
        });
        _repository.SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (SynchronizationContext.Current is RecordingContext) onCaller.Add("save");
            return Task.CompletedTask;
        });
        _store.Exists(Arg.Any<string>()).Returns(_ =>
        {
            if (SynchronizationContext.Current is RecordingContext) onCaller.Add("storage check");
            return false;
        });
        var statuses = new List<string>();
        var percents = new List<double>();
        var changes = new PendingChanges([new UpdateSource("", [OnDisk("", "new.bin", 1, "N.bin")], [])], [], []);

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            await CreateService().ApplyAsync(
                "IIDX 32", changes,
                percent: new InlineProgress<double>(percents.Add),
                status: new InlineProgress<string>(statuses.Add),
                ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        onCaller.Should().BeEmpty("the UI never freezes: none of this may run on the calling thread");
        statuses.First().Should().StartWith("Checking");
        statuses.Should().Contain(s => s.StartsWith("Saving"), "one big save has no steps to count, so it has to be named");
        percents.Should().Contain(100).And.BeInAscendingOrder();
    }

    /// <summary>Reports inline; <see cref="Progress{T}"/> would post to a context and arrive late.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }

    [Fact]
    public async Task The_low_disk_question_is_asked_on_the_callers_thread()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Existing());
        _driveInfo.GetAvailableFreeSpace("/data/db").Returns(1_000L);
        var context = new RecordingContext();
        var askedOn = -1;
        _dialogs.ConfirmAsync(Arg.Any<string>(), "Low disk space").Returns(_ =>
        {
            askedOn = SynchronizationContext.Current is RecordingContext ? 1 : 0;
            return false;
        });
        var changes = new PendingChanges([new UpdateSource("", [OnDisk("", "new.bin", 500, "N.bin")], [])], [], []);

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            await CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // The real dialog opens a window, which only the UI thread may do.
        askedOn.Should().Be(1);
    }

    /// <summary>Stands in for the UI thread's context: continuations posted to it run with it current.</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                d(state);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Low_disk_space_asks_before_copying(bool proceed)
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Existing());
        _driveInfo.GetAvailableFreeSpace("/data/db").Returns(1_000L);
        _dialogs.ConfirmAsync(Arg.Any<string>(), "Low disk space").Returns(proceed);
        var changes = new PendingChanges([new UpdateSource("", [OnDisk("", "new.bin", 500, "N.bin")], [])], [], []);

        var result = await CreateService().ApplyAsync("IIDX 32", changes, ct: TestContext.Current.CancellationToken);

        result.Success.Should().Be(proceed);
        result.Cancelled.Should().Be(!proceed);
        await _repository.Received(proceed ? 1 : 0).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_reports_a_missing_instance()
    {
        _repository.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((Instance?)null);

        var result = await CreateService().ApplyAsync("gone", new PendingChanges([], [], []), ct: TestContext.Current.CancellationToken);

        result.Error.Should().Be("Library entry 'gone' not found.");
    }
}
