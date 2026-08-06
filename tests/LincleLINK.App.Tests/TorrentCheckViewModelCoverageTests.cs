using System.Collections.ObjectModel;
using System.Security.Cryptography;
using FluentAssertions;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Abstractions.Torrents;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Torrents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// Remaining <see cref="TorrentCheckViewModel"/> branches: the browse commands,
/// the piece-verification success path, and command gating on host busy state.
/// </summary>
public sealed class TorrentCheckViewModelCoverageTests
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IOperationHost _host = Substitute.For<IOperationHost>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly ITorrentSource _source = Substitute.For<ITorrentSource>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();

    public TorrentCheckViewModelCoverageTests()
    {
        _host.LogLines.Returns(new ObservableCollection<string>());
        _host.When(h => h.AddLogLine(Arg.Any<string>()))
            .Do(ci => _host.LogLines.Add(ci.Arg<string>()!));
        _host.RunOperationAsync(Arg.Any<string>(), Arg.Any<Func<OperationContext, Task>>())
            .Returns(ci => ci.Arg<Func<OperationContext, Task>>()!(new OperationContext(
                new InlineProgress<string>(), new InlineProgress<string>(), new InlineProgress<double>(), CancellationToken.None)));
    }

    private TorrentCheckViewModel CreateViewModel() =>
        new(new TorrentService(_source, _repository, _store, Substitute.For<IHardLinker>(), _fs, NullLogger<TorrentService>.Instance), _dialogs, _preflight, _host);

    [Fact]
    public async Task Browse_torrent_file_sets_the_path()
    {
        _dialogs.PickOpenFileAsync(Arg.Any<string>(), Arg.Any<FileType>()).Returns("C:\\x.torrent");
        var vm = CreateViewModel();

        await vm.BrowseTorrentFileCommand.ExecuteAsync(null);

        vm.TorrentFilePath.Should().Be("C:\\x.torrent");
    }

    [Fact]
    public async Task Browse_download_path_sets_the_folder()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\dl");
        var vm = CreateViewModel();

        await vm.BrowseTorrentDlPathCommand.ExecuteAsync(null);

        vm.TorrentDownloadPath.Should().Be("C:\\dl");
    }

    [Fact]
    public void Browse_commands_are_gated_on_host_busy()
    {
        var vm = CreateViewModel();

        vm.BrowseTorrentFileCommand.CanExecute(null).Should().BeTrue();
        vm.BrowseTorrentDlPathCommand.CanExecute(null).Should().BeTrue();

        _host.IsBusy.Returns(true);
        vm.BrowseTorrentFileCommand.CanExecute(null).Should().BeFalse();
        vm.BrowseTorrentDlPathCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CheckPieces_success_computes_the_link_button_text()
    {
        var content = new byte[] { 1, 2, 3, 4 };
        _source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentData(
                "fixture", 4, 4,
                [SHA1.HashData(content)],
                [new TorrentFileData("contents/data.bin", 4)]));
        _repository.GetAsync("X", Arg.Any<CancellationToken>())
            .Returns(Instance.Create("X", [new InstanceFile("data.bin", "", 4, "A".PadRight(32, 'A') + ".bin")], [""]));
        _store.GetPath("A".PadRight(32, 'A') + ".bin").Returns("C:\\db\\A" + ".bin");
        _fs.OpenRead("C:\\db\\A.bin").Returns(_ => new MemoryStream(content));

        var vm = CreateViewModel();
        vm.TorrentInstance = new InstanceListEntry("X", 0, 0, "0 B");
        vm.TorrentFilePath = "x.torrent";
        vm.RelativePath = "contents";
        vm.FilesMatched = true;

        await vm.CheckPiecesCommand.ExecuteAsync(null);

        vm.PiecesVerified.Should().BeTrue();
        vm.VerifySummary.Should().Contain("1 of 1 pieces verified");
        vm.LinkButtonText.Should().Be("Link 1 files");
    }

    private sealed class InlineProgress<T>(Action<T>? handler = null) : IProgress<T>
    {
        public void Report(T value) => handler?.Invoke(value);
    }
}
