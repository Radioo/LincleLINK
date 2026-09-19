using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;

namespace LincleLINK.App.ViewModels;

/// <summary>
/// One Source in the files dialog's source list (plan 16 D5): what was dropped,
/// where it lands, and whether it is still being hashed.
/// </summary>
public sealed partial class StagedSourceViewModel : ObservableObject
{
    private readonly Action<StagedSourceViewModel> _remove;
    private readonly Action _changed;
    private UpdateSource _scanned;

    internal StagedSourceViewModel(
        string label,
        SourceScan scan,
        string destination,
        CancellationToken dialogClosed,
        Action<StagedSourceViewModel> remove,
        Action changed)
    {
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(dialogClosed);
        Label = label;
        _scanned = scan.Source;
        _destination = destination;
        _remove = remove;
        _changed = changed;
        Issues = scan.Issues;
        Contents = $"{scan.Source.Files.Count} files, {SizeFormatter.Format(scan.Source.Files.Sum(f => f.FileSize))}";
    }

    /// <summary>The dropped folder's name, the single file's name, or "N items".</summary>
    public string Label { get; }

    public string Contents { get; }

    /// <summary>Skipped links and unreadable files or folders of this Source.</summary>
    public IReadOnlyList<SourceIssue> Issues { get; private set; }

    public bool HasIssues => Issues.Count > 0;

    public string IssuesText => string.Join(Environment.NewLine, Issues.Select(i => $"{i.Path}: {i.Message}"));

    /// <summary>
    /// One line for the source row, e.g. "3 items were left out: link (Skipped, link to
    /// D:\x), ...". The full list is the tooltip (<see cref="IssuesText"/>): a list of
    /// its own would need a scroll container, and only the file tree scrolls.
    /// </summary>
    public string IssuesSummary
    {
        get
        {
            if (Issues.Count == 0)
            {
                return string.Empty;
            }

            var first = string.Join(", ", Issues.Take(2).Select(i => $"{i.Path} ({i.Message})"));
            var more = Issues.Count > 2 ? $", and {Issues.Count - 2} more" : string.Empty;
            return $"{Issues.Count} {(Issues.Count == 1 ? "item was" : "items were")} left out: {first}{more}";
        }
    }

    /// <summary>Only a single dropped folder can stay a subfolder; other Sources have no folder of their own.</summary>
    public bool HasTopFolder => _scanned.TopFolder is not null;

    /// <summary>Stops this Source's hashing: when it is removed, or when the dialog closes.</summary>
    internal CancellationTokenSource Cancellation { get; }

    /// <summary>Folder inside the instance where this Source lands; empty for the root.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationText))]
    private string _destination;

    /// <summary>Keep the dropped folder as a subfolder instead of merging its contents into the Destination.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationText))]
    private bool _keepTopFolder;

    [ObservableProperty]
    private bool _isHashing = true;

    [ObservableProperty]
    private double _hashProgress;

    /// <summary>Where the Source ends up, e.g. "-> modules/" or "-> (entry root)".</summary>
    public string DestinationText
    {
        get
        {
            var path = PathNormalizer.Canonicalize(
                KeepTopFolder && _scanned.TopFolder is { } top ? $"{Destination}/{top}" : Destination);
            return path.Length == 0 ? "-> (entry root)" : $"-> {path}/";
        }
    }

    /// <summary>The Source as the planner takes it, with the user's Destination choices applied.</summary>
    internal UpdateSource Source => _scanned with { Destination = Destination, KeepTopFolder = KeepTopFolder };

    internal UpdateSource Scanned => _scanned;

    internal void SetHashed(SourceScan hashed)
    {
        _scanned = hashed.Source;
        Issues = [.. Issues, .. hashed.Issues];
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssuesText));
        OnPropertyChanged(nameof(IssuesSummary));
        IsHashing = false;
    }

    partial void OnDestinationChanged(string? oldValue, string newValue)
    {
        // The box takes free text. Deploy refuses rooted and '..' paths, so an
        // entry holding one could never be deployed again: put the old value back.
        if (!PathNormalizer.IsSafeRelativePath(newValue))
        {
            Destination = oldValue ?? string.Empty;
            return;
        }

        _changed();
    }

    partial void OnKeepTopFolderChanged(bool value) => _changed();

    [RelayCommand]
    private void Remove()
    {
        Cancellation.Cancel();
        _remove(this);
    }

    [RelayCommand]
    private void MoveToRoot() => Destination = string.Empty;
}
