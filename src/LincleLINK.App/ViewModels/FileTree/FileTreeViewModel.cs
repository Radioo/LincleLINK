using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;
using LincleLINK.Core.Infrastructure.Collections;

namespace LincleLINK.App.ViewModels.FileTree;

/// <summary>
/// An instance's files as a tree flattened to its visible rows (plan 16 D11), so a
/// virtualizing list can show it at any size. It shows the instance as an Update
/// plan leaves it (D6): an instance without Pending changes is a plan where every
/// file is unchanged. Paths match case-insensitively (ADR 0001).
/// </summary>
/// <summary>What the user has open and filtered in the tree at one moment.</summary>
public sealed record TreeViewState(HashSet<string> OpenFolders, string Filter, bool ChangesOnly)
{
    public bool IsFiltering => ChangesOnly || Filter.Length > 0;
}

/// <summary>A tree built off the UI thread, ready to be shown with one reset of the rows.</summary>
public sealed class PreparedTree
{
    internal PreparedTree(FileTreeRow root, List<FileTreeRow> visibleRows, bool hasChanges, TreeViewState view)
    {
        Root = root;
        VisibleRows = visibleRows;
        HasChanges = hasChanges;
        View = view;
    }

    internal FileTreeRow Root { get; }

    internal List<FileTreeRow> VisibleRows { get; }

    /// <summary>Whether the plan behind the tree changes anything.</summary>
    public bool HasChanges { get; }

    internal TreeViewState View { get; }
}

public sealed partial class FileTreeViewModel : ObservableObject
{
    private FileTreeRow _root = new(string.Empty, null, null);

    /// <summary>
    /// Paths of the folders that were open before a filter took over the expanded
    /// state; null while no filter is active.
    /// </summary>
    private HashSet<string>? _expandedBeforeFilter;

    /// <summary>
    /// Case-insensitive name filter. Matches show with the folders leading to them
    /// opened; a matching folder keeps its contents reachable. Clearing the filter
    /// restores the expanded state from before.
    /// </summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>Hides every row the Pending changes leave as it is.</summary>
    [ObservableProperty]
    private bool _changesOnly;

    /// <summary>Whether the loaded plan changes anything.</summary>
    [ObservableProperty]
    private bool _hasChanges;

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnChangesOnlyChanged(bool value) => ApplyFilter();

    /// <summary>The rows currently visible, in display order.</summary>
    public AvaloniaList<FileTreeRow> Rows { get; } = [];

    private bool IsFiltering => ChangesOnly || Filter.Trim().Length > 0;

    /// <summary>Shows an instance as it is, with no Pending changes.</summary>
    public void Load(IEnumerable<InstanceFile> files, IEnumerable<string> directories)
        => Load(new UpdatePlan(
            files.Select(f => new PlannedFile(f, PlannedStatus.Unchanged)).ToList(), directories.ToList(), [], []));

    /// <summary>
    /// Shows the instance as <paramref name="plan"/> leaves it. Folders that were
    /// open stay open, since the plan is recomputed after every staging step.
    /// Building the tree of a large entry takes a moment, so from the UI use
    /// <see cref="LoadAsync"/>; this one does all of it on the calling thread.
    /// </summary>
    public void Load(UpdatePlan plan) => Show(Build(plan, CaptureView()));

    /// <summary>
    /// The same, with the tree built on the thread pool (CLAUDE.md: the UI never
    /// freezes). Only the swap of the visible rows happens on the calling thread.
    /// </summary>
    public async Task LoadAsync(UpdatePlan plan)
    {
        var view = CaptureView();
        Show(await Task.Run(() => Build(plan, view)));
    }

    /// <summary>What the user has open and filtered right now. Call on the UI thread.</summary>
    public TreeViewState CaptureView()
        => new(_expandedBeforeFilter ?? CollectExpanded(_root, NewPathSet()), Filter.Trim(), ChangesOnly);

    /// <summary>
    /// Builds the tree for <paramref name="plan"/> and the rows that
    /// <paramref name="view"/> makes visible. Touches nothing that is bound to the
    /// UI, so it can run on any thread.
    /// </summary>
    public static PreparedTree Build(UpdatePlan plan, TreeViewState view)
    {
        var root = new FileTreeRow(string.Empty, null, null);
        var byPath = new Dictionary<string, FileTreeRow>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = root,
        };

        foreach (var directory in plan.Directories)
        {
            MarkAlive(GetOrAddDirectory(byPath, PathNormalizer.Canonicalize(directory)));
        }

        foreach (var directory in plan.RemovedDirectories)
        {
            GetOrAddDirectory(byPath, PathNormalizer.Canonicalize(directory)).IsRemoved = true;
        }

        // An empty folder from a Source is a change with no file to carry it.
        foreach (var directory in plan.AddedDirectories)
        {
            GetOrAddDirectory(byPath, PathNormalizer.Canonicalize(directory)).IsNewFolder = true;
        }

        var hasChanges = plan.RemovedDirectories.Count > 0 || plan.AddedDirectories.Count > 0;
        foreach (var planned in plan.Files)
        {
            var parent = GetOrAddDirectory(byPath, PathNormalizer.Canonicalize(planned.File.RelativePath));
            var row = new FileTreeRow(planned.File.FileName, parent, planned);
            parent.Children.Add(row);
            hasChanges |= row.IsChange;
            if (planned.IsInResult)
            {
                MarkAlive(parent);
            }
        }

        // The folder half of a collision is in the lookup; the file half is a child of its parent.
        foreach (var collision in plan.Collisions)
        {
            var split = collision.Path.LastIndexOf('/');
            if (byPath.TryGetValue(collision.Path, out var folder)
                && byPath.TryGetValue(split < 0 ? string.Empty : collision.Path[..split], out var parent))
            {
                folder.IsCollision = true;
                foreach (var file in parent.Children.Where(c => !c.IsDirectory
                             && string.Equals(c.Path, collision.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    file.IsCollision = true;
                }
            }
        }

        Finish(root, hasChanges);
        Restore(root, view.OpenFolders);
        if (view.IsFiltering)
        {
            MarkMatches(root, view.Filter, view.ChangesOnly, inherited: false);
        }

        var visible = new List<FileTreeRow>();
        AppendVisibleDescendants(root, visible);
        return new PreparedTree(root, visible, hasChanges, view);
    }

    /// <summary>Puts a built tree on screen with one reset of the rows. Call on the UI thread.</summary>
    public void Show(PreparedTree prepared)
    {
        _root = prepared.Root;
        HasChanges = prepared.HasChanges;
        _expandedBeforeFilter = prepared.View.IsFiltering ? prepared.View.OpenFolders : null;

        if (prepared.View.Filter != Filter.Trim() || prepared.View.ChangesOnly != ChangesOnly)
        {
            // The user changed a filter while the tree was being built.
            ApplyFilter();
            return;
        }

        Rows.Clear();
        Rows.AddRange(prepared.VisibleRows);
    }

    /// <summary>Opens every folder on the way to a change and nothing else (plan 16 D6).</summary>
    public void ExpandChanges()
    {
        OpenWhere(_root, static row => row.ChangeCount > 0);
        Rebuild();
    }

    public void ExpandAll()
    {
        OpenWhere(_root, static _ => true);
        Rebuild();
    }

    private static void OpenWhere(FileTreeRow directory, Func<FileTreeRow, bool> shouldOpen)
    {
        foreach (var child in directory.Children)
        {
            if (child.IsDirectory)
            {
                child.IsExpanded = shouldOpen(child);
                OpenWhere(child, shouldOpen);
            }
        }
    }

    private void ApplyFilter()
    {
        if (IsFiltering)
        {
            _expandedBeforeFilter ??= CollectExpanded(_root, NewPathSet());
            MarkMatches(_root, Filter.Trim(), ChangesOnly, inherited: false);
        }
        else if (_expandedBeforeFilter is { } expanded)
        {
            _expandedBeforeFilter = null;
            Restore(_root, expanded);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        var visible = new List<FileTreeRow>();
        AppendVisibleDescendants(_root, visible);
        Rows.Clear();
        Rows.AddRange(visible);
    }

    private static HashSet<string> NewPathSet() => new(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> CollectExpanded(FileTreeRow directory, HashSet<string> expanded)
    {
        foreach (var child in directory.Children)
        {
            if (child.IsDirectory)
            {
                if (child.IsExpanded)
                {
                    expanded.Add(child.Path);
                }

                CollectExpanded(child, expanded);
            }
        }

        return expanded;
    }

    /// <summary>
    /// Hides what the filters reject and opens the folders that lead to a match.
    /// Returns whether anything below <paramref name="directory"/> matches.
    /// </summary>
    private static bool MarkMatches(FileTreeRow directory, string text, bool changesOnly, bool inherited)
    {
        var any = false;
        foreach (var child in directory.Children)
        {
            var nameMatches = text.Length > 0 && child.Name.Contains(text, StringComparison.OrdinalIgnoreCase);

            // A folder passes "changes only" through what it holds, except a removed
            // folder, which is a change even when it is empty.
            var passesChanges = !changesOnly
                || (child.IsDirectory ? child.IsRemoved || child.IsNewFolder || child.IsCollision : child.IsChange);
            var matches = (text.Length == 0 || nameMatches) && passesChanges;

            // Below a folder that matched by name everything stays reachable, but
            // only a match of its own opens the folders leading to it.
            var reachable = inherited && passesChanges;
            var below = child.IsDirectory && MarkMatches(child, text, changesOnly, inherited || nameMatches);

            child.IsHidden = !(matches || reachable || below);
            if (child.IsDirectory)
            {
                child.IsExpanded = below;
            }

            any |= matches || below;
        }

        return any;
    }

    private static void Restore(FileTreeRow directory, HashSet<string> expanded)
    {
        foreach (var child in directory.Children)
        {
            child.IsHidden = false;
            if (child.IsDirectory)
            {
                child.IsExpanded = expanded.Contains(child.Path);
                Restore(child, expanded);
            }
        }
    }

    /// <summary>
    /// Shows a folder's children. They arrive as one range insert: a folder can hold
    /// tens of thousands of files, and one event per row would stall the list.
    /// </summary>
    public void Expand(FileTreeRow row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0 || !row.IsDirectory || row.IsExpanded)
        {
            return;
        }

        row.IsExpanded = true;
        var visible = new List<FileTreeRow>();
        AppendVisibleDescendants(row, visible);
        Rows.InsertRange(index + 1, visible);
    }

    /// <summary>
    /// Hides everything below a folder in one range removal. Nested folders keep
    /// their expanded state, so expanding again restores the same view.
    /// </summary>
    public void Collapse(FileTreeRow row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0 || !row.IsExpanded)
        {
            return;
        }

        row.IsExpanded = false;
        var end = index + 1;
        while (end < Rows.Count && Rows[end].Depth > row.Depth)
        {
            end++;
        }

        Rows.RemoveRange(index + 1, end - index - 1);
    }

    public void Toggle(FileTreeRow row)
    {
        if (row.IsExpanded)
        {
            Collapse(row);
        }
        else
        {
            Expand(row);
        }
    }

    /// <summary>Right arrow: expand a collapsed folder, else move to its first child. Returns the row to select.</summary>
    public FileTreeRow StepInto(FileTreeRow row)
    {
        if (!row.IsDirectory)
        {
            return row;
        }

        if (!row.IsExpanded)
        {
            Expand(row);
            return row;
        }

        return row.Children.FirstOrDefault(c => !c.IsHidden) ?? row;
    }

    /// <summary>Left arrow: collapse an expanded folder, else move to the parent. Returns the row to select.</summary>
    public FileTreeRow StepOut(FileTreeRow row)
    {
        if (row.IsExpanded)
        {
            Collapse(row);
            return row;
        }

        return row.Parent ?? row;
    }

    private static void AppendVisibleDescendants(FileTreeRow directory, List<FileTreeRow> visible)
    {
        foreach (var child in directory.Children)
        {
            if (child.IsHidden)
            {
                continue;
            }

            visible.Add(child);
            if (child.IsExpanded)
            {
                AppendVisibleDescendants(child, visible);
            }
        }
    }

    private static FileTreeRow GetOrAddDirectory(Dictionary<string, FileTreeRow> byPath, string path)
    {
        if (byPath.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var split = path.LastIndexOf('/');
        var parent = GetOrAddDirectory(byPath, split < 0 ? string.Empty : path[..split]);
        var row = new FileTreeRow(path[(split + 1)..], parent, null);
        parent.Children.Add(row);
        byPath[path] = row;
        return row;
    }

    /// <summary>A folder that stays keeps every folder above it.</summary>
    private static void MarkAlive(FileTreeRow directory)
    {
        for (var row = directory; row is not null && !row.IsAlive; row = row.Parent)
        {
            row.IsAlive = true;
        }
    }

    /// <summary>
    /// Sorts every level (folders first, then natural name order), totals the
    /// folders, and settles what a folder that does not stay is: removed, or the
    /// home of nothing but unticked Source files.
    /// </summary>
    private static void Finish(FileTreeRow directory, bool dimUnchanged)
    {
        var removedBelow = false;
        foreach (var child in directory.Children)
        {
            if (child.IsDirectory)
            {
                Finish(child, dimUnchanged);

                // A new folder with nothing new inside counts as one change itself.
                directory.ChangeCount += child is { IsNewFolder: true, ChangeCount: 0 } ? 1 : child.ChangeCount;
            }
            else
            {
                child.IsDimmed = dimUnchanged && !child.IsChange;
                if (child.IsChange && !child.IsExcluded)
                {
                    directory.ChangeCount++;
                }
            }

            removedBelow |= child.IsRemoved;
            directory.Size += child.Size;
            directory.FileCount += child.FileCount;
        }

        if (directory.Depth >= 0 && !directory.IsAlive)
        {
            directory.IsRemoved |= removedBelow;
            directory.IsExcluded = !directory.IsRemoved;
        }

        directory.IsDimmed = dimUnchanged && directory.ChangeCount == 0 && !directory.IsChange;

        directory.Children.Sort(static (a, b) => a.IsDirectory != b.IsDirectory
            ? (a.IsDirectory ? -1 : 1)
            : NaturalStringComparer.Instance.Compare(a.Name, b.Name));
    }
}
