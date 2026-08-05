using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Tests.TestHelpers;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Games;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class AddInstanceViewModelTests
{
    // Platform-native mock paths (Path.GetRelativePath/GetDirectoryName are OS-sensitive).
    private static string Data => Path.Combine(Path.GetTempPath(), "data");
    private static string FileA => Path.Combine(Data, "a.bin");
    private static string StoreA => "C:\\db\\AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin";

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

    private AddInstanceViewModel Create() =>
        new(
            new InstanceService(_fs, _hasher, _store, _hardLinker, _preflight, _repository, _driveInfo, _dialogs, _detector),
            _dialogs, _taskbarProgress, _fs, _preflight, _detector);

    private void StubDataPath()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([FileA]);
        _fs.GetFileLength(FileA).Returns(100);
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        _store.Exists(Arg.Any<string>()).Returns(false);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1_000_000_000_000L);
    }

    [Fact]
    public async Task CreateInstance_success_raises_close()
    {
        StubDataPath();
        var vm = Create();
        var closeRequested = false;
        vm.CloseRequested += (_, _) => closeRequested = true;
        vm.InstanceName = "inst";
        vm.DataPath = Data;

        await vm.CreateInstanceCommand.ExecuteAsync(null);

        closeRequested.Should().BeTrue();
        vm.LogLines.Should().Contain(m => m.StartsWith(LogMessages.EntryAdded, StringComparison.Ordinal));
        await _repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateInstance_error_shows_error_and_does_not_close()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([]);

        var vm = Create();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.InstanceName = "inst";
        vm.DataPath = Data;

        await vm.CreateInstanceCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ErrorAsync(Arg.Any<string>(), Arg.Any<string>());
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task Move_mode_copies_to_db_and_hard_links_back()
    {
        StubDataPath();
        _store.GetPath(Arg.Any<string>()).Returns(StoreA);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var vm = Create();
        vm.InstanceName = "inst";
        vm.DataPath = Data;
        vm.IsKeepChecked = false;
        vm.IsReclaimChecked = true;

        await vm.CreateInstanceCommand.ExecuteAsync(null);

        await _store.Received(1).CopyToStoreAsync(FileA, Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Safe reclaim ordering: temp link first, then swapped over the original.
        _hardLinker.Received(1).TryCreateLink(
            StoreA,
            Arg.Is<string>(p => p != null && p.StartsWith(FileA, StringComparison.Ordinal) && p.EndsWith(".lincletmp", StringComparison.Ordinal)),
            out _);
        _fs.Received(1).MoveFile(
            Arg.Is<string>(p => p != null && p.EndsWith(".lincletmp", StringComparison.Ordinal)), FileA, true);
    }

    [Fact]
    public void Browse_command_picks_folder_and_sets_data_path()
    {
        _dialogs.PickFolderAsync("Select the folder to add").Returns("C:\\chosen");
        var vm = Create();

        vm.BrowseCommand.ExecuteAsync(null);

        vm.DataPath.Should().Be("C:\\chosen");
    }

    [Fact]
    public void Fresh_instance_starts_with_defaults()
    {
        var vm = Create();

        vm.InstanceName.Should().BeEmpty();
        vm.DataPath.Should().BeEmpty();
        // Reclaim space is the recommended default (plan 14 §2).
        vm.IsReclaimChecked.Should().BeTrue();
        vm.IsKeepChecked.Should().BeFalse();
        vm.ReclaimAvailable.Should().BeTrue();
        vm.LogLines.Should().BeEmpty();
        vm.Progress.Should().Be(0);
    }

    [Fact]
    public async Task Cross_volume_folder_disables_reclaim_and_falls_back_to_keep()
    {
        _fs.DirectoryExists(Data).Returns(true);
        _fs.EnumerateFiles(Data, true).Returns([FileA]);
        _fs.GetFileLength(FileA).Returns(100);
        _preflight.CheckLinkTo(Data).Returns("The folder is on a different drive than storage.");

        var vm = Create();
        vm.DataPath = Data;

        // The analysis runs in the background; wait for its outcome.
        await AsyncWaits.AwaitUntilAsync(() => !vm.ReclaimAvailable);

        vm.ReclaimAvailable.Should().BeFalse();
        vm.CrossVolumeReason.Should().Contain("different drive");
        vm.IsKeepChecked.Should().BeTrue();
        vm.IsReclaimChecked.Should().BeFalse();
    }
}
