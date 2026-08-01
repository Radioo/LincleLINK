using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class InstanceServiceTests
{
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private InstanceService CreateService() => new(_fs, _hasher, _store, _repository, _driveInfo, _dialogs);

    private void StubDataPath(string dataPath, params string[] files)
    {
        _fs.DirectoryExists(dataPath).Returns(true);
        _fs.EnumerateFiles(dataPath, true).Returns(files);
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
        _fs.DirectoryExists("C:\\data").Returns(true);
        var result = await CreateService().CreateInstanceAsync(new("bad/name", "C:\\data", CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_data_path_returns_error()
    {
        _fs.DirectoryExists("C:\\missing").Returns(false);
        var result = await CreateService().CreateInstanceAsync(new("ok", "C:\\missing", CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Data path");
    }

    [Fact]
    public async Task Duplicate_name_returns_error()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin");
        _repository.ExistsAsync("dupe", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().CreateInstanceAsync(new("dupe", "C:\\data", CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Empty_data_path_returns_error()
    {
        _fs.DirectoryExists("C:\\data").Returns(true);
        _fs.EnumerateFiles("C:\\data", true).Returns([]);

        var result = await CreateService().CreateInstanceAsync(new("ok", "C:\\data", CopyMoveMode.Copy));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no files");
    }

    [Fact]
    public async Task Copy_mode_counts_added_and_existing()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin", "C:\\data\\b.bin", "C:\\data\\sub\\c.bin");
        _store.Exists("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(false, true, false);

        var result = await CreateService().CreateInstanceAsync(new("inst", "C:\\data", CopyMoveMode.Copy));

        result.Success.Should().BeTrue();
        result.FilesAdded.Should().Be(2);
        result.AlreadyExisted.Should().Be(1);
        result.TotalFiles.Should().Be(3);
        result.BytesAdded.Should().Be(200);

        await _store.Received(2).CopyToStoreAsync(Arg.Any<string>(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin", Arg.Any<CancellationToken>());
        await _store.DidNotReceive().MoveToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Move_mode_moves_and_dedup_leaves_source_in_place()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin");
        _store.Exists("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin").Returns(true);

        var result = await CreateService().CreateInstanceAsync(new("inst", "C:\\data", CopyMoveMode.Move));

        result.Success.Should().BeTrue();
        result.AlreadyExisted.Should().Be(1);
        result.FilesAdded.Should().Be(0);
        await _store.DidNotReceive().MoveToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Low_disk_confirm_proceeds_and_decline_aborts()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin");
        _driveInfo.GetAvailableFreeSpace("C:\\data").Returns(100L); // smaller than 100 + wiggle

        var confirm = true;
        _dialogs.Confirm(Arg.Any<string>(), Arg.Any<string>()).Returns(_ => confirm);

        var proceed = await CreateService().CreateInstanceAsync(new("inst", "C:\\data", CopyMoveMode.Copy));
        proceed.Success.Should().BeTrue();

        confirm = false;
        var declined = await CreateService().CreateInstanceAsync(new("inst2", "C:\\data", CopyMoveMode.Copy));
        declined.Success.Should().BeFalse();
        declined.Error.Should().BeNull();

        // Only the "proceed" call saves; the declined call must not add another.
        await _repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Low_disk_check_skipped_in_move_mode()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin");
        _driveInfo.GetAvailableFreeSpace("C:\\data").Returns(10L);

        var result = await CreateService().CreateInstanceAsync(new("inst", "C:\\data", CopyMoveMode.Move));

        result.Success.Should().BeTrue();
        _driveInfo.DidNotReceiveWithAnyArgs().GetAvailableFreeSpace(default!);
    }

    [Fact]
    public async Task Directories_collected_as_relative_paths()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin", "C:\\data\\sub\\b.bin");

        Instance? saved = null;
        _repository.SaveAsync(Arg.Do<Instance>(i => saved = i), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await CreateService().CreateInstanceAsync(new("inst", "C:\\data", CopyMoveMode.Copy));

        saved.Should().NotBeNull();
        saved!.FileList.Should().HaveCount(2);
        saved.FileList[0].RelativePath.Should().Be(string.Empty);
        saved.FileList[1].RelativePath.Should().Be(Path.Combine("sub"));
        saved.DirectoryList.Should().Contain(string.Empty).And.Contain(Path.Combine("sub"));
    }

    [Fact]
    public async Task Progress_reports_log_lines_and_percent()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin");
        var logs = new List<string>();
        double lastPercent = 0;
        var log = new SynchronousProgress<string>(logs.Add);
        var percent = new SynchronousProgress<double>(p => lastPercent = p);

        await CreateService().CreateInstanceAsync(new("inst", "C:\\data", CopyMoveMode.Copy), log, percent);

        logs.Should().Contain("Hashing...");
        logs.Should().Contain(m => m.StartsWith("Instance added.", StringComparison.Ordinal));
        lastPercent.Should().Be(100);
    }

    [Fact]
    public async Task Cancellation_mid_loop_does_not_save()
    {
        StubDataPath("C:\\data", "C:\\data\\a.bin", "C:\\data\\b.bin");
        _store.Exists(Arg.Any<string>()).Returns(false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => CreateService().CreateInstanceAsync(new("inst", "C:\\data", CopyMoveMode.Copy), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _repository.DidNotReceive().SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }
}
