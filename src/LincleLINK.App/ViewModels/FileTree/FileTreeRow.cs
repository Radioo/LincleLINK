using CommunityToolkit.Mvvm.ComponentModel;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;

namespace LincleLINK.App.ViewModels.FileTree;

/// <summary>
/// One file or folder of an instance (plan 16 D11). The same object is the tree
/// node and, while visible, the list row: <see cref="Depth"/> drives indentation.
/// </summary>
public sealed partial class FileTreeRow : ObservableObject
{
    private readonly FileTreeRow? _parent;

    internal FileTreeRow(string name, FileTreeRow? parent, PlannedFile? planned)
    {
        Name = name;
        _parent = parent;
        Planned = planned;
        Depth = parent is null ? -1 : parent.Depth + 1;
        Path = parent is null || parent.Path.Length == 0 ? name : $"{parent.Path}/{name}";

        if (planned is not null)
        {
            Status = planned.Status;
            IsRemoved = planned.Status == PlannedStatus.Removed;
            IsExcluded = planned.Status == PlannedStatus.Excluded || planned.HasExcludedSource;
            if (planned.IsInResult)
            {
                Size = planned.File.FileSize;
                FileCount = 1;
            }
        }
    }

    public string Name { get; }

    /// <summary>Path inside the instance with '/' separators, as the staging calls take it.</summary>
    public string Path { get; }

    /// <summary>Null for rows directly under the instance root.</summary>
    public FileTreeRow? Parent => _parent is { Depth: >= 0 } ? _parent : null;

    /// <summary>The planned file behind a file row; null for folders.</summary>
    public PlannedFile? Planned { get; }

    /// <summary>The entry as stored, for a file row.</summary>
    public InstanceFile? File => Planned?.File;

    public bool IsDirectory => Planned is null;

    public int Depth { get; }

    /// <summary>Left indentation of the row in the flattened list, in pixels.</summary>
    public double Indent => Depth * 16;

    /// <summary>Size after Apply: the file's, or the total of every file left below a folder.</summary>
    public long Size { get; internal set; }

    /// <summary>Files after Apply: 1 for a file that stays, or the count below a folder.</summary>
    public int FileCount { get; internal set; }

    public string SizeText => SizeFormatter.Format(Size);

    /// <summary>What the Pending changes do to a file row. Folders stay <see cref="PlannedStatus.Unchanged"/>.</summary>
    public PlannedStatus Status { get; }

    /// <summary>Staged for Removal: a file, or a folder that goes with everything below it.</summary>
    public bool IsRemoved { get; internal set; }

    /// <summary>
    /// The user unticked the Source content at this path: a Source file that stays
    /// out, a kept file whose replacement stays out, or a folder holding only such files.
    /// </summary>
    public bool IsExcluded { get; internal set; }

    /// <summary>Files below a folder that the Pending changes add, replace or remove.</summary>
    public int ChangeCount { get; internal set; }

    /// <summary>Stays as it is while other rows change, so the view can push it back.</summary>
    public bool IsDimmed { get; internal set; }

    /// <summary>A folder that a Source introduces.</summary>
    public bool IsNewFolder { get; internal set; }

    /// <summary>A file and a folder both claim this path, which blocks Apply.</summary>
    public bool IsCollision { get; internal set; }

    public bool IsAdded => Status == PlannedStatus.Added || IsNewFolder;

    public bool IsReplaced => Status == PlannedStatus.Replaced;

    public bool IsIdentical => Status == PlannedStatus.Identical;

    /// <summary>A Source file at an existing path that still waits for its hash.</summary>
    public bool IsChecking => Status == PlannedStatus.Pending;

    /// <summary>An earlier Source held this path too and lost to a later one.</summary>
    public bool OverridesEarlierSource => Planned?.OverridesEarlierSource == true;

    public bool HasChangeCount => ChangeCount > 0;

    /// <summary>Old and new size of a replaced file, for the tooltip; null otherwise.</summary>
    public string? Detail => Planned is { Status: PlannedStatus.Replaced, Previous: { } previous }
        ? $"{SizeFormatter.Format(previous.FileSize)} -> {SizeFormatter.Format(Planned.File.FileSize)}"
        : null;

    /// <summary>The row itself is something the Pending changes touch.</summary>
    internal bool IsChange => IsRemoved || IsExcluded || IsNewFolder || IsCollision
        || Status is PlannedStatus.Added or PlannedStatus.Replaced or PlannedStatus.Pending;

    internal List<FileTreeRow> Children { get; } = [];

    /// <summary>Filtered out of the visible rows.</summary>
    internal bool IsHidden { get; set; }

    /// <summary>A folder that the resulting instance still has.</summary>
    internal bool IsAlive { get; set; }

    [ObservableProperty]
    private bool _isExpanded;
}
