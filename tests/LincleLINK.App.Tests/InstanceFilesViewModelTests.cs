using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.Services;
using LincleLINK.Core.Abstractions.Disk;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Hashing;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class InstanceFilesViewModelTests
{
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private InstanceFilesViewModel CreateViewModel()
        => new(
            _repository, _store, _dialogs,
            new InstanceUpdateService(
                Substitute.For<IFileSystem>(), Substitute.For<IFileHasher>(), _store, _repository,
                Substitute.For<IDriveInfoProvider>(), Substitute.For<IAppPaths>(), _dialogs,
                Substitute.For<LincleLINK.Core.Abstractions.Games.IGameVersionDetector>(),
                NullLogger<InstanceUpdateService>.Instance),
            Substitute.For<ITaskbarProgress>(), NullLogger<InstanceFilesViewModel>.Instance);

    [Fact]
    public async Task LoadAsync_shows_the_entrys_files_as_a_tree_with_a_summary()
    {
        _repository.GetAsync("IIDX 32", Arg.Any<CancellationToken>()).Returns(Instance.Create(
            "IIDX 32",
            [new InstanceFile("bm2dx.dll", "modules", 2048, "AA.dll"), new InstanceFile("readme.txt", "", 1024, "BB.txt")],
            ["modules"]));
        var vm = CreateViewModel();

        await vm.LoadAsync("IIDX 32", TestContext.Current.CancellationToken);

        vm.InstanceName.Should().Be("IIDX 32");
        vm.Tree.Rows.Select(r => r.Name).Should().Equal("modules", "readme.txt");
        vm.Summary.Should().Be("2 files, 3 KB");
    }

    [Fact]
    public async Task LoadAsync_reports_a_missing_entry_and_closes()
    {
        _repository.GetAsync("gone", Arg.Any<CancellationToken>()).Returns((Instance?)null);
        var vm = CreateViewModel();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.LoadAsync("gone", TestContext.Current.CancellationToken);

        await _dialogs.Received(1).ErrorAsync("Library entry 'gone' not found.", "Browse files");
        closed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false, "explorer.exe", @"/select,""C:\db\AA.dll""")]
    [InlineData(false, true, "open", @"-R|C:\db\AA.dll")]
    public void Reveal_selects_the_file_in_the_platform_file_manager(
        bool isWindows, bool isMacOS, string command, string arguments)
    {
        var info = FolderOpener.CreateRevealStartInfo(@"C:\db\AA.dll", isWindows, isMacOS);

        info.FileName.Should().Be(command);
        (info.ArgumentList.Count > 0 ? string.Join('|', info.ArgumentList) : info.Arguments)
            .Should().Be(arguments);
    }

    [Fact]
    public void Reveal_opens_the_containing_folder_where_no_file_manager_can_select()
    {
        var path = Path.Combine(Path.GetTempPath(), "db", "AA.dll");

        var info = FolderOpener.CreateRevealStartInfo(path, isWindows: false, isMacOS: false);

        info.FileName.Should().Be("xdg-open");
        info.ArgumentList.Should().Equal(Path.GetDirectoryName(path));
    }
}
