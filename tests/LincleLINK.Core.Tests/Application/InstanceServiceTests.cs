using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class InstanceServiceTests
{
    // Platform-native mock paths (GetRelativePath/GetDirectoryName are OS-sensitive).
    private static string Data => Path.Combine(Path.GetTempPath(), "data");
    private static string Missing => Path.Combine(Path.GetTempPath(), "missing");
    private static string FileA => Path.Combine(Data, "a.bin");
    private static string FileB => Path.Combine(Data, "b.bin");
    private static string FileSubC => Path.Combine(Data, "sub", "c.bin");
    private static string StoreA => "C:\\db\\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin";

    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    // The preflight substitute returns null (= linkable) by default, matching the
    // common same-volume case; cross-volume tests override it explicitly.
    private InstanceService CreateService()
        => new(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, NullLogger<InstanceService>.Instance);

    private void StubDataPath(string dataPath, params string[] files)
    {
        _fs.DirectoryExists(dataPath).Returns(true);
        _fs.EnumerateFiles(dataPath, true).Returns(files);
        _fs.EnumerateDirectories(dataPath, true).Returns([]);
        foreach (var file in files)
        {
            _fs.GetFileLength(file).Returns(100);
        }

        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        // Default: plenty of space so the low-disk path never triggers unless a
        // specific test overrides it. Confirm is only stubbed by low-disk tests.
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1_000_000_000_000L);
    }

    [Fact]
    public async Task Invalid_name_returns_error_without_io()
    {
        _fs.DirectoryExists(Data).Returns(true);
        var result = await CreateService().CreateInstanceAsync(new("bad/name", Data, CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_data_path_returns_error()
    {
        _fs.DirectoryExists(Missing).Returns(false);
        var result = await CreateService().CreateInstanceAsync(new("ok", Missing, CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("folder does not exist");
    }

    [Fact]
    public async Task Duplicate_name_returns_error()
    {
        StubDataPath(Data, FileA);
        _repository.ExistsAsync("dupe", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().CreateInstanceAsync(new("dupe", Data, CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Empty_data_path_returns_error()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([]);

        var result = await CreateService().CreateInstanceAsync(new("ok", Data, CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no files");
    }

    [Fact]
    public async Task Copy_mode_counts_added_and_existing()
    {
        StubDataPath(Data, FileA, FileB, FileSubC);
        _store.Exists("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(false, true, false);

        var result = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Copy));

        result.Success.Should().BeTrue();
        result.FilesAdded.Should().Be(2);
        result.AlreadyExisted.Should().Be(1);
        result.TotalFiles.Should().Be(3);
        result.BytesAdded.Should().Be(200);

        await _store.Received(2).CopyToStoreAsync(Arg.Any<string>(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Move_mode_copies_to_db_and_hard_links_back()
    {
        StubDataPath(Data, FileA);
        _store.Exists("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(false);
        _store.GetPath("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(StoreA);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var result = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Move));

        result.Success.Should().BeTrue();
        result.FilesAdded.Should().Be(1);
        result.BytesAdded.Should().Be(100);

        await _store.Received(1).CopyToStoreAsync(FileA, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", Arg.Any<CancellationToken>());
        // Link-then-replace order (plan 14 D3): the link lands at a temp name and
        // is swapped over the original; the original is never deleted up front.
        _hardLinker.Received(1).TryCreateLink(
            StoreA,
            Arg.Is<string>(p => p != null && p.StartsWith(FileA, StringComparison.Ordinal) && p.EndsWith(".lincletmp", StringComparison.Ordinal)),
            out _);
        _fs.Received(1).MoveFile(
            Arg.Is<string>(p => p != null && p.EndsWith(".lincletmp", StringComparison.Ordinal)), FileA, true);
        _fs.DidNotReceive().DeleteFile(FileA);
    }

    [Fact]
    public async Task Move_mode_dedup_links_original_to_existing_db_file()
    {
        StubDataPath(Data, FileA);
        _store.Exists("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(true);
        _store.GetPath("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(StoreA);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var result = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Move));

        result.Success.Should().BeTrue();
        result.AlreadyExisted.Should().Be(1);
        result.FilesAdded.Should().Be(0);

        // The duplicate original becomes a hard link to the existing db file
        // (temp link swapped over the original).
        _hardLinker.Received(1).TryCreateLink(
            StoreA,
            Arg.Is<string>(p => p != null && p.StartsWith(FileA, StringComparison.Ordinal) && p.EndsWith(".lincletmp", StringComparison.Ordinal)),
            out _);
        _fs.Received(1).MoveFile(
            Arg.Is<string>(p => p != null && p.EndsWith(".lincletmp", StringComparison.Ordinal)), FileA, true);
    }

    [Fact]
    public async Task Move_mode_failed_link_leaves_original_untouched()
    {
        StubDataPath(Data, FileA);
        _store.Exists("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(false);
        _store.GetPath("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(StoreA);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = "Access denied.";
            return false;
        });

        var logs = new List<string>();
        var log = new SynchronousProgress<string>(logs.Add);

        var result = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Move), log);

        result.Success.Should().BeTrue();
        // No data loss: the original file is never deleted or overwritten when the
        // link could not be created.
        _fs.DidNotReceive().DeleteFile(FileA);
        _fs.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default);
        logs.Should().Contain(m => m.Contains("left unchanged", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Move_mode_cross_volume_preflight_fails_fast()
    {
        StubDataPath(Data, FileA);
        _preflight.CheckLinkTo(Data).Returns("The folder is on a different drive than storage.");

        var result = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Move));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Can't reclaim space");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
        _hardLinker.DidNotReceiveWithAnyArgs().TryCreateLink(default!, default!, out _);
    }

    [Fact]
    public async Task Low_disk_confirm_proceeds_and_decline_aborts()
    {
        StubDataPath(Data, FileA);
        _driveInfo.GetAvailableFreeSpace(Data).Returns(100L); // smaller than 100 + wiggle

        var confirm = true;
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(_ => confirm);

        var proceed = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Copy));
        proceed.Success.Should().BeTrue();

        confirm = false;
        var declined = await CreateService().CreateInstanceAsync(new("inst2", Data, CopyMoveMode.Copy));
        declined.Success.Should().BeFalse();
        declined.Error.Should().BeNull();

        // Only the "proceed" call saves; the declined call must not add another.
        await _repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Low_disk_check_skipped_in_move_mode()
    {
        StubDataPath(Data, FileA);
        _driveInfo.GetAvailableFreeSpace(Data).Returns(10L);

        var result = await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Move));

        result.Success.Should().BeTrue();
        _driveInfo.DidNotReceiveWithAnyArgs().GetAvailableFreeSpace(default!);
    }

    [Fact]
    public async Task Directories_collected_as_relative_paths()
    {
        StubDataPath(Data, FileA, Path.Combine(Data, "sub", "b.bin"));

        Instance? saved = null;
        _repository.SaveAsync(Arg.Do<Instance>(i => saved = i), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Copy));

        saved.Should().NotBeNull();
        saved!.FileList.Should().HaveCount(2);
        saved.FileList[0].RelativePath.Should().Be(string.Empty);
        saved.FileList[1].RelativePath.Should().Be(Path.Combine("sub"));
        saved.DirectoryList.Should().Contain(string.Empty).And.Contain(Path.Combine("sub"));
    }

    [Fact]
    public async Task Empty_directories_are_preserved_in_DirectoryList()
    {
        StubDataPath(Data, FileA);
        _fs.EnumerateDirectories(Data, true).Returns(
        [
            Path.Combine(Data, "empty"),
            Path.Combine(Data, "sub", "nested"),
        ]);

        Instance? saved = null;
        _repository.SaveAsync(Arg.Do<Instance>(i => saved = i), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Copy));

        saved.Should().NotBeNull();
        // Directories that contain no files must still be recorded (v2 parity).
        saved!.DirectoryList.Should().Contain(Path.Combine("empty"));
        saved.DirectoryList.Should().Contain(Path.Combine("sub", "nested"));
    }

    [Fact]
    public async Task Progress_reports_log_lines_and_percent()
    {
        StubDataPath(Data, FileA);
        var logs = new List<string>();
        var statuses = new List<string>();
        var progressLock = new object();
        double lastPercent = 0;
        var log = new SynchronousProgress<string>(s => { lock (progressLock) logs.Add(s); });
        var status = new SynchronousProgress<string>(s => { lock (progressLock) statuses.Add(s); });
        var percent = new SynchronousProgress<double>(p => lastPercent = p);

        await CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Copy), log, percent, status);

        logs.Should().Contain(m => m.StartsWith("Hashing ", StringComparison.Ordinal));      // header
        statuses.Should().Contain(m => m.StartsWith("Hashed ", StringComparison.Ordinal));   // per-file completion
        statuses.Should().Contain(m => m.StartsWith("Added ", StringComparison.Ordinal));    // store decision
        logs.Should().Contain(m => m.StartsWith(LogMessages.EntryAdded, StringComparison.Ordinal));
        lastPercent.Should().Be(100);
    }

    [Fact]
    public async Task MaxDegree_of_parallelism_one_hashes_sequentially()
    {
        StubDataPath(Data, FileA, FileB, FileSubC);
        _store.Exists(Arg.Any<string>()).Returns(false);

        int active = 0;
        int maxActive = 0;
        var gate = new object();

        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var now = Interlocked.Increment(ref active);
                lock (gate)
                {
                    maxActive = Math.Max(maxActive, now);
                }

                try
                {
                    await Task.Delay(20);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }

                return "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
            });

        var result = await CreateService().CreateInstanceAsync(
            new("inst", Data, CopyMoveMode.Copy, MaxDegreeOfParallelism: 1));

        result.Success.Should().BeTrue();
        maxActive.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_mid_loop_does_not_save()
    {
        StubDataPath(Data, FileA, FileB);
        _store.Exists(Arg.Any<string>()).Returns(false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => CreateService().CreateInstanceAsync(new("inst", Data, CopyMoveMode.Copy), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }
}

