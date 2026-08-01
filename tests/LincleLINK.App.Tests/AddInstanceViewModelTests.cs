using FluentAssertions;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class AddInstanceViewModelTests
{
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileHasher _hasher = Substitute.For<IFileHasher>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDriveInfoProvider _driveInfo = Substitute.For<IDriveInfoProvider>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private AddInstanceViewModel Create() =>
        new(new InstanceService(_fs, _hasher, _store, _repository, _driveInfo, _dialogs), _dialogs);

    private void StubDataPath()
    {
        _fs.DirectoryExists("C:\\data").Returns(true);
        _fs.EnumerateFiles("C:\\data", true).Returns(["C:\\data\\a.bin"]);
        _fs.GetFileLength("C:\\data\\a.bin").Returns(100);
        _hasher.ComputeHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        _store.Exists(Arg.Any<string>()).Returns(false);
        _driveInfo.GetAvailableFreeSpace(Arg.Any<string>()).Returns(1_000_000_000_000L);
    }

    [Fact]
    public async Task MakeInstance_success_raises_close()
    {
        StubDataPath();
        var vm = Create();
        var closeRequested = false;
        vm.CloseRequested += (_, success) => closeRequested = success;
        vm.InstanceName = "inst";
        vm.DataPath = "C:\\data";

        await vm.MakeInstanceCommand.ExecuteAsync(null);

        closeRequested.Should().BeTrue();
        vm.LogLines.Should().Contain(m => m.StartsWith("Instance added.", StringComparison.Ordinal));
        await _repository.Received(1).SaveAsync(Arg.Any<Instance>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MakeInstance_error_shows_error_and_does_not_close()
    {
        _fs.DirectoryExists("C:\\data").Returns(true);
        _fs.EnumerateFiles("C:\\data", true).Returns([]);

        var vm = Create();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;
        vm.InstanceName = "inst";
        vm.DataPath = "C:\\data";

        await vm.MakeInstanceCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ErrorAsync(Arg.Any<string>(), Arg.Any<string>());
        closed.Should().BeFalse();
    }

    [Fact]
    public async Task Move_mode_uses_move_operations()
    {
        StubDataPath();
        var vm = Create();
        vm.InstanceName = "inst";
        vm.DataPath = "C:\\data";
        vm.IsCopyChecked = false;
        vm.IsMoveChecked = true;

        await vm.MakeInstanceCommand.ExecuteAsync(null);

        await _store.Received(1).MoveToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().CopyToStoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Browse_command_picks_folder_and_sets_data_path()
    {
        _dialogs.PickFolderAsync("Select data path").Returns("C:\\chosen");
        var vm = Create();

        vm.BrowseCommand.ExecuteAsync(null);

        vm.DataPath.Should().Be("C:\\chosen");
    }
}
