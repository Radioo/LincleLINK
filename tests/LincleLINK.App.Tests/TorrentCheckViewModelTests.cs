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

    public TorrentCheckViewModelTests()
    {
        _host.SelectedInstanceName.Returns("X");
        _host.LogLines.Returns(new ObservableCollection<string>());
        _host.RunOperationAsync(Arg.Any<Func<IProgress<string>, IProgress<double>, Task>>())
            .Returns(ci => ci.Arg<Func<IProgress<string>, IProgress<double>, Task>>()!(
                new InlineProgress<string>(), new InlineProgress<double>()));
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
        return new TorrentCheckViewModel(service, _dialogs, _host);
    }

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
        vm.TorrentFilePath = "x.torrent";
        vm.RelativePath = "contents";

        await vm.CheckFilesCommand.ExecuteAsync(null);

        vm.PiecesChecked.Should().BeTrue();
        vm.MatchedFiles.Should().BeEquivalentTo(["data.bin"]);
        vm.CheckPiecesCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task CheckFiles_success_zero_matches_keeps_piece_gate_off_and_logs_hint()
    {
        var vm = CreateViewModel(
            SourceWithFiles(("contents/data.bin", 10)),
            RepositoryWithInstance(InstanceWithFile("data.bin", 99)));
        vm.TorrentFilePath = "x.torrent";
        vm.RelativePath = "contents";

        await vm.CheckFilesCommand.ExecuteAsync(null);

        vm.PiecesChecked.Should().BeFalse();
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
        vm.TorrentFilePath = "x.torrent";
        vm.RelativePath = "contents";

        await vm.CheckFilesCommand.ExecuteAsync(null);

        vm.PiecesChecked.Should().BeFalse();
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
        vm.TorrentFilePath = "x.torrent";
        vm.TorrentDownloadPath = "C:\\dl";
        vm.PiecesChecked = true;

        await vm.CheckPiecesCommand.ExecuteAsync(null);

        vm.LinkReady.Should().BeFalse();
        vm.LinkToTorrentCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LinkToTorrent_after_run_resets_all_gates()
    {
        var vm = CreateViewModel();
        vm.TorrentFilePath = "x.torrent";
        vm.TorrentDownloadPath = "C:\\dl";
        vm.PiecesChecked = true;
        vm.LinkReady = true;

        await vm.LinkToTorrentCommand.ExecuteAsync(null);

        vm.PiecesChecked.Should().BeFalse();
        vm.LinkReady.Should().BeFalse();
        vm.CheckPiecesCommand.CanExecute(null).Should().BeFalse();
        vm.LinkToTorrentCommand.CanExecute(null).Should().BeFalse();
    }

    private sealed class InlineProgress<T>(Action<T>? handler = null) : IProgress<T>
    {
        public void Report(T value) => handler?.Invoke(value);
    }
}
