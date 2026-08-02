using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Application;
using LincleLINK.Core.Application.Torrents;
using LincleLINK.Core.Domain;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// The "Link to torrent" tab's state machine: torrent/relative/download paths,
/// piece-check gates, matched-file list, and the check/pieces/link commands.
/// Split out of <see cref="MainViewModel"/> so the shell VM orchestrates features
/// instead of implementing them all.
/// </summary>
public partial class TorrentCheckViewModel : ViewModelBase
{
    private readonly TorrentService _torrentService;
    private readonly IDialogService _dialogs;
    private readonly IOperationHost _host;

    private IReadOnlyList<TorrentFileCheck> _checkedFiles = [];
    private IReadOnlyList<long> _badPieces = [];

    public ObservableCollection<string> MatchedFiles { get; } = [];

    /// <summary>
    /// The instance linked from, picked in the torrent tab's own selector. Deliberately
    /// independent of <c>MainViewModel.SelectedInstance</c> (which serves the Instances
    /// tab): the two selections have different purposes and must not cross-write each
    /// other through shared two-way bindings.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckFilesCommand))]
    private InstanceListEntry? _torrentInstance;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckFilesCommand))]
    private string _torrentFilePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckFilesCommand))]
    private string _relativePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckFilesCommand), nameof(LinkToTorrentCommand))]
    private string _torrentDownloadPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckPiecesCommand), nameof(LinkToTorrentCommand))]
    private bool _piecesChecked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LinkToTorrentCommand))]
    private bool _linkReady;

    public TorrentCheckViewModel(
        TorrentService torrentService,
        IDialogService dialogs,
        IOperationHost host)
    {
        _torrentService = torrentService;
        _dialogs = dialogs;
        _host = host;
    }

    partial void OnTorrentFilePathChanged(string value) => ResetGates();
    partial void OnRelativePathChanged(string value) => ResetGates();
    partial void OnTorrentDownloadPathChanged(string value) => ResetGates();

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseTorrentFileAsync()
    {
        var path = await _dialogs.PickOpenFileAsync(
            "Select a torrent file", new FileType("Torrent files", ["*.torrent"]));
        if (path is not null)
        {
            TorrentFilePath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseTorrentDlPathAsync()
    {
        var path = await _dialogs.PickFolderAsync("Select torrent download and link target location");
        if (path is not null)
        {
            TorrentDownloadPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckFiles))]
    private async Task CheckFilesAsync()
    {
        await _host.RunOperationAsync(async (log, percent) =>
        {
            var result = await _torrentService.CheckFilesAsync(
                new TorrentCheckRequest(TorrentInstance!.InstanceName, TorrentFilePath, RelativePath), log, percent);

            if (!result.Success)
            {
                if (result.Error is not null)
                {
                    _host.LogLines.Add(result.Error);
                }

                PiecesChecked = false;
                return;
            }

            MatchedFiles.Clear();
            foreach (var path in result.MatchedFilePaths)
            {
                MatchedFiles.Add(path);
            }

            PiecesChecked = result.Matched > 0;
            if (result.Matched == 0)
            {
                _host.LogLines.Add(LogMessages.RelativePathHint);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCheckPieces))]
    private async Task CheckPiecesAsync()
    {
        await _host.RunOperationAsync(async (log, percent) =>
        {
            var result = await _torrentService.CheckPiecesAsync(
                new TorrentCheckRequest(TorrentInstance!.InstanceName, TorrentFilePath, RelativePath), log, percent);

            if (!result.Success)
            {
                if (result.Error is not null)
                {
                    _host.LogLines.Add(result.Error);
                }

                LinkReady = false;
                _checkedFiles = [];
                _badPieces = [];
                return;
            }

            _checkedFiles = result.Files;
            _badPieces = result.BadPieces;
            LinkReady = !string.IsNullOrWhiteSpace(TorrentDownloadPath);
        });
    }

    [RelayCommand(CanExecute = nameof(CanLinkToTorrent))]
    private async Task LinkToTorrentAsync()
    {
        await _host.RunOperationAsync(async (log, percent) =>
        {
            var result = await _torrentService.LinkToTorrentAsync(
                new LinkToTorrentRequest(TorrentDownloadPath, _checkedFiles, _badPieces), log, percent);

            if (result.Error is not null)
            {
                _host.LogLines.Add(result.Error);
            }
        });

        PiecesChecked = false;
        LinkReady = false;
        _checkedFiles = [];
        _badPieces = [];
    }

    private bool CanBrowse() => !_host.IsBusy;

    private bool CanCheckFiles()
        => !_host.IsBusy
           && TorrentInstance is not null
           && !string.IsNullOrWhiteSpace(TorrentFilePath);

    private bool CanCheckPieces() => !_host.IsBusy && PiecesChecked;

    private bool CanLinkToTorrent() => !_host.IsBusy && LinkReady;

    private void ResetGates()
    {
        PiecesChecked = false;
        LinkReady = false;
    }
}
