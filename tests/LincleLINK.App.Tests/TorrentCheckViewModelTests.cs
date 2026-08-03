using System.Collections.ObjectModel;
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
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class TorrentCheckViewModelTests
{
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IOperationHost _host = Substitute.For<IOperationHost>();

    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();

    public TorrentCheckViewModelTests()
    {
        _host.LogLines.Returns(new ObservableCollection<string>());
        _host.RunOperationAsync(Arg.Any<Func<OperationContext, Task>>())
            .Returns(ci => ci.Arg<Func<OperationContext, Task>>()!(new OperationContext(
                new InlineProgress<string>(),
                new InlineProgress<string>(),
                new InlineProgress<double>(),
                CancellationToken.None)));
    }

    private TorrentCheckViewModel CreateViewModel(
        ITorrentSource? source = null,
        IInstanceRepository? repository = null)
    {
        var service = new TorrentService(
            source ?? Substitute.For<ITorrentSource>(),
            repository ?? Substitute.For<IInstanceRepository>(),
            Substitute.For<IFileStore>(),
            Substitute.For<IHardLinker>(),
            Substitute.For<IFileSystem>());
        return new TorrentCheckViewModel(service, _dialogs, _preflight, _host);
    }

    private static void SelectInstance(TorrentCheckViewModel vm) =>
        vm.TorrentInstance = new InstanceListEntry("X", 0, 0, "0 B");

    private static IInstanceRepository RepositoryWithInstance(Instance instance)
    {
        var repository = Substitute.For<IInstanceRepository>();
        repository.GetAsync(instance.InstanceName, Arg.Any<CancellationToken>()).Returns(instance);
        return repository;
    }

    /// <summary>
    /// Instance whose files live under a "contents" subdir, so the torrent request
    /// uses RelativePath="contents" and strips it before comparing.
    /// </summary>
    private static Instance InstanceWithFile(string fileName, long size)
        => Instance.Create("X", [new InstanceFile(fileName, "", size, "A".PadRight(32, 'A') + ".bin")], [""]);

    private static ITorrentSource SourceWithFiles(params (string Path, long Length)[] files)
    {
        var source = Substitute.For<ITorrentSource>();
        source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentData(
                "fixture",
                files.Sum(f => f.Length),
                16,
                [new byte[20]],
                files.Select(f => new TorrentFileData(f.Path, f.Length)).ToList()));
        return source;
    }

    [Fact]
    public async Task CheckFiles_success_populates_matched_files_and_sets_piece_gate()
    {
        var vm = CreateViewModel(
            SourceWithFiles(("contents/data.bin", 10)),
            RepositoryWithInstance(InstanceWithFile("data.bin", 10)));
        SelectInstance(vm);
        vm.TorrentFilePath = "x.torrent";
        vm.RelativePath = "contents";

        await vm.CheckFilesCommand.ExecuteAsync(null);

        vm.FilesMatched.Should().BeTrue();
        vm.MatchedFiles.Should().BeEquivalentTo(["data.bin"]);
        vm.MatchSummary.Should().Contain("1 of 1");
        vm.VerifyHint.Should().BeEmpty();
        vm.CheckPiecesCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task CheckFiles_success_zero_matches_keeps_piece_gate_off_and_logs_hint()
    {
        var vm = CreateViewModel(
            SourceWithFiles(("contents/data.bin", 10)),
            RepositoryWithInstance(InstanceWithFile("data.bin", 99)));
        SelectInstance(vm);
        vm.TorrentFilePath = "x.torrent";
        vm.RelativePath = "contents";

        await vm.CheckFilesCommand.ExecuteAsync(null);

        vm.FilesMatched.Should().BeFalse();
        vm.MatchedFiles.Should().BeEmpty();
        _host.LogLines.Should().Contain(l => l.Contains(LogMessages.RelativePathHint));
    }

    [Fact]
    public async Task CheckFiles_failure_resets_piece_gate_and_logs_error()
    {
        var source = Substitute.For<ITorrentSource>();
        source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TorrentData>(_ => throw new TorrentNotSupportedException("no v2"));
        var vm = CreateViewModel(
            source,
            RepositoryWithInstance(InstanceWithFile("data.bin", 10)));
        SelectInstance(vm);
        vm.TorrentFilePath = "x.torrent";
        vm.RelativePath = "contents";

        await vm.CheckFilesCommand.ExecuteAsync(null);

        vm.FilesMatched.Should().BeFalse();
        _host.LogLines.Should().Contain(l => l.Contains("v2"));
        vm.CheckPiecesCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CheckPieces_failure_keeps_link_gate_off_and_clears_state()
    {
        var source = Substitute.For<ITorrentSource>();
        source.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TorrentData>(_ => throw new TorrentNotSupportedException("no v2"));
        var vm = CreateViewModel(
            source,
            RepositoryWithInstance(InstanceWithFile("data.bin", 10)));
        SelectInstance(vm);
        vm.TorrentFilePath = "x.torrent";
        vm.TorrentDownloadPath = "C:\\dl";
        vm.FilesMatched = true;

        await vm.CheckPiecesCommand.ExecuteAsync(null);

        vm.PiecesVerified.Should().BeFalse();
        vm.LinkToTorrentCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LinkToTorrent_after_run_resets_gates_but_keeps_summary()
    {
        var vm = CreateViewModel();
        vm.TorrentFilePath = "x.torrent";
        vm.TorrentDownloadPath = "C:\\dl";
        vm.FilesMatched = true;
        vm.PiecesVerified = true;

        await vm.LinkToTorrentCommand.ExecuteAsync(null);

        vm.FilesMatched.Should().BeFalse();
        vm.PiecesVerified.Should().BeFalse();
        vm.LinkSummary.Should().Contain("Linked");
        vm.CheckPiecesCommand.CanExecute(null).Should().BeFalse();
        vm.LinkToTorrentCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LinkToTorrent_cross_volume_preflight_blocks_with_error_dialog()
    {
        var vm = CreateViewModel();
        vm.TorrentFilePath = "x.torrent";
        vm.TorrentDownloadPath = "D:\\dl";
        vm.FilesMatched = true;
        vm.PiecesVerified = true;
        _preflight.CheckLinkTo("D:\\dl").Returns("The folder is on a different drive than storage.");

        await vm.LinkToTorrentCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ErrorAsync(
            Arg.Is<string>(m => m != null && m.Contains("different drive")), Arg.Any<string>());
        await _host.DidNotReceiveWithAnyArgs().RunOperationAsync(default!);
        // Gates stay intact so the user can retry with a different folder.
        vm.PiecesVerified.Should().BeTrue();
    }

    [Fact]
    public void Changing_download_path_does_not_invalidate_verification()
    {
        var vm = CreateViewModel();
        vm.FilesMatched = true;
        vm.PiecesVerified = true;

        vm.TorrentDownloadPath = "C:\\somewhere-else";

        vm.FilesMatched.Should().BeTrue();
        vm.PiecesVerified.Should().BeTrue();
        vm.LinkToTorrentCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Changing_torrent_file_resets_gates_and_summaries()
    {
        var vm = CreateViewModel();
        vm.FilesMatched = true;
        vm.PiecesVerified = true;
        vm.MatchSummary = "1 of 1 files matched.";

        vm.TorrentFilePath = "other.torrent";

        vm.FilesMatched.Should().BeFalse();
        vm.PiecesVerified.Should().BeFalse();
        vm.MatchSummary.Should().BeEmpty();
        vm.VerifyHint.Should().Be("Match files first.");
    }

    private sealed class InlineProgress<T>(Action<T>? handler = null) : IProgress<T>
    {
        public void Report(T value) => handler?.Invoke(value);
    }
}
