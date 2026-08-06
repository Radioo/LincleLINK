using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.App.Abstractions;
using LincleLINK.App.ViewModels.Base;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Application;
using LincleLINK.Core.Application.Torrents;
using LincleLINK.Core.Domain;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// The torrent pre-fill tab as a visible three-step wizard (plan 14 D7):
/// match files → verify pieces → link verified files. Gate properties describe
/// their own state (<see cref="FilesMatched"/>, <see cref="PiecesVerified"/>);
/// per-step summaries and lock hints render inline next to each step.
/// Split out of <see cref="MainViewModel"/> so the shell VM orchestrates features
/// instead of implementing them all.
/// </summary>
public partial class TorrentCheckViewModel : ViewModelBase
{
    private readonly TorrentService _torrentService;
    private readonly IDialogService _dialogs;
    private readonly IHardLinkPreflight _preflight;
    private readonly IOperationHost _host;

    private IReadOnlyList<TorrentFileCheck> _checkedFiles = [];
    private IReadOnlyList<long> _badPieces = [];

    public ObservableCollection<string> MatchedFiles { get; } = [];

    /// <summary>
    /// The library entry linked from, picked in this tab's own selector. Deliberately
    /// independent of <c>MainViewModel.SelectedInstance</c> (which serves the Library
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

    /// <summary>Step 1 succeeded with at least one match; unlocks Verify pieces.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckPiecesCommand), nameof(LinkToTorrentCommand))]
    private bool _filesMatched;

    /// <summary>Step 2 succeeded; unlocks linking (with a chosen download folder).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LinkToTorrentCommand))]
    private bool _piecesVerified;

    /// <summary>Step results ("2,113 of 2,480 files matched"); empty until the step ran.</summary>
    [ObservableProperty]
    private string _matchSummary = string.Empty;

    [ObservableProperty]
    private string _verifySummary = string.Empty;

    [ObservableProperty]
    private string _linkSummary = string.Empty;

    /// <summary>Why the step is locked ("Match files first."); empty when unlocked.</summary>
    [ObservableProperty]
    private string _verifyHint = "Match files first.";

    [ObservableProperty]
    private string _linkHint = "Verify pieces first.";

    /// <summary>
    /// Step-3 button label; states the exact effect once verification computed the
    /// eligible file count (plan 15 D6), e.g. "Link 2,096 files".
    /// </summary>
    [ObservableProperty]
    private string _linkButtonText = "Link verified files";

    public TorrentCheckViewModel(
        TorrentService torrentService,
        IDialogService dialogs,
        IHardLinkPreflight preflight,
        IOperationHost host)
    {
        _torrentService = torrentService;
        _dialogs = dialogs;
        _preflight = preflight;
        _host = host;
    }

    // Match/verify results depend on the entry, the torrent, and the relative
    // path - invalidate them when any of those change. The download folder is
    // only a link-time input; changing it must NOT reset verification (plan 14 D7).
    partial void OnTorrentInstanceChanged(InstanceListEntry? value) => ResetGates();
    partial void OnTorrentFilePathChanged(string value) => ResetGates();
    partial void OnRelativePathChanged(string value) => ResetGates();
    partial void OnTorrentDownloadPathChanged(string value) => UpdateHints();

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
        var path = await _dialogs.PickFolderAsync("Select the torrent download folder");
        if (path is not null)
        {
            TorrentDownloadPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckFiles))]
    private async Task CheckFilesAsync()
    {
        await _host.RunOperationAsync("Check torrent files", async op =>
        {
            var result = await _torrentService.CheckFilesAsync(
                new TorrentCheckRequest(TorrentInstance!.InstanceName, TorrentFilePath, RelativePath),
                op.Log, op.Percent, op.CancellationToken);

            if (!result.Success)
            {
                if (result.Error is not null)
                {
                    await _dialogs.ErrorAsync(result.Error, "Check torrent files");
                }

                FilesMatched = false;
                UpdateHints();
                return;
            }

            MatchedFiles.Clear();
            foreach (var path in result.MatchedFilePaths)
            {
                MatchedFiles.Add(path);
            }

            MatchSummary = result.Matched > 0
                ? $"{result.Matched} of {result.Total} files matched."
                : "No files matched - check your relative path.";
            FilesMatched = result.Matched > 0;

            UpdateHints();
        });
    }

    [RelayCommand(CanExecute = nameof(CanCheckPieces))]
    private async Task CheckPiecesAsync()
    {
        await _host.RunOperationAsync("Check torrent pieces", async op =>
        {
            var result = await _torrentService.CheckPiecesAsync(
                new TorrentCheckRequest(TorrentInstance!.InstanceName, TorrentFilePath, RelativePath),
                op.Log, op.Percent, op.CancellationToken);

            if (!result.Success)
            {
                if (result.Error is not null)
                {
                    await _dialogs.ErrorAsync(result.Error, "Check torrent pieces");
                }

                PiecesVerified = false;
                _checkedFiles = [];
                _badPieces = [];
                UpdateHints();
                return;
            }

            _checkedFiles = result.Files;
            _badPieces = result.BadPieces;
            VerifySummary = $"{result.MatchedPieces} of {result.TotalPieces} pieces verified.";
            PiecesVerified = true;

            var bad = result.BadPieces.ToHashSet();
            var eligible = result.Files.Count(f =>
                f.HashedFileName is not null && !f.Pieces.Any(bad.Contains));
            LinkButtonText = $"Link {eligible} files";
            UpdateHints();
        });
    }

    [RelayCommand(CanExecute = nameof(CanLinkToTorrent))]
    private async Task LinkToTorrentAsync()
    {
        // One clear cross-volume failure up front instead of one per file (plan 14 D2).
        var preflightError = await Task.Run(() => _preflight.CheckLinkTo(TorrentDownloadPath));
        if (!string.IsNullOrEmpty(preflightError))
        {
            await _dialogs.ErrorAsync(
                $"Can't link into this folder: {preflightError}",
                "Pre-fill a torrent download");
            return;
        }

        await _host.RunOperationAsync("Link to torrent", async op =>
        {
            var result = await _torrentService.LinkToTorrentAsync(
                new LinkToTorrentRequest(TorrentDownloadPath, _checkedFiles, _badPieces),
                op.Log, op.Percent, op.CancellationToken);

            if (result.Error is not null)
            {
                await _dialogs.ErrorAsync(result.Error, "Link to torrent");
                return;
            }

            LinkSummary = $"Linked {result.Linked} files, skipped {result.Skipped}.";
        });

        // Linked files now exist at the target, so previous match/verify results
        // are stale; the summary of what was linked stays visible.
        ResetGates(keepLinkSummary: true);
    }

    private bool CanBrowse() => !_host.IsBusy;

    private bool CanCheckFiles()
        => !_host.IsBusy
           && TorrentInstance is not null
           && !string.IsNullOrWhiteSpace(TorrentFilePath);

    private bool CanCheckPieces() => !_host.IsBusy && FilesMatched;

    private bool CanLinkToTorrent()
        => !_host.IsBusy
           && PiecesVerified
           && !string.IsNullOrWhiteSpace(TorrentDownloadPath);

    private void ResetGates(bool keepLinkSummary = false)
    {
        FilesMatched = false;
        PiecesVerified = false;
        MatchSummary = string.Empty;
        VerifySummary = string.Empty;
        LinkButtonText = "Link verified files";
        if (!keepLinkSummary)
        {
            LinkSummary = string.Empty;
        }

        _checkedFiles = [];
        _badPieces = [];
        UpdateHints();
    }

    private void UpdateHints()
    {
        VerifyHint = FilesMatched ? string.Empty : "Match files first.";
        LinkHint = !PiecesVerified
            ? "Verify pieces first."
            : string.IsNullOrWhiteSpace(TorrentDownloadPath)
                ? "Choose a download folder above."
                : string.Empty;
    }
}
