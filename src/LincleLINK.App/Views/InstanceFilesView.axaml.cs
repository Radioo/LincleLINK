using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LincleLINK.App.ViewModels;
using LincleLINK.App.ViewModels.FileTree;

namespace LincleLINK.App.Views;

public partial class InstanceFilesView : UserControl
{
    public InstanceFilesView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private InstanceFilesViewModel? Files => DataContext as InstanceFilesViewModel;

    // ── drops (plan 16 D5) ────────────────────────────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accepts = Files is { IsBusy: false } && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        DropZone.Classes.Set("Hot", accepts);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DropZone.Classes.Set("Hot", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Set("Hot", false);
        if (Files is not { IsBusy: false } files)
        {
            return;
        }

        var paths = (e.DataTransfer.TryGetFiles() ?? [])
            .Select(item => item.TryGetLocalPath())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToList();

        // A drop on a row lands in that row's folder; anywhere else is the entry root.
        var row = FileTreeView.RowOf(e.Source);
        var destination = row is null ? string.Empty
            : row.IsDirectory ? row.Path
            : row.Parent?.Path ?? string.Empty;

        _ = files.AddPathsAsync(paths, destination);
    }

    // ── row actions (plan 16 D6, D7) ──────────────────────────────────────

    private void OnTreeMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var rows = TreeView.ContextRows;
        var idle = Files is { IsBusy: false };

        RemoveItem.IsEnabled = idle && rows.Any(InstanceFilesViewModel.CanRemove);
        ExcludeItem.IsEnabled = idle && rows.Any(InstanceFilesViewModel.CanExclude);
        RestoreItem.IsEnabled = idle && rows.Any(r => r.IsRemoved || r.IsExcluded || r.ChangeCount > 0);

        // Only a file that is already in Storage has something to reveal.
        RevealItem.IsEnabled = TreeView.ContextRow is { IsDirectory: false, File.HashedFileName.Length: > 0 };
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
        => _ = StageAsync(TreeView.ContextRows, (files, rows) => files.StageRemovalAsync(rows));

    private void OnExcludeClick(object? sender, RoutedEventArgs e)
        => _ = StageAsync(TreeView.ContextRows, (files, rows) => files.StageExclusionAsync(rows));

    private void OnRestoreClick(object? sender, RoutedEventArgs e)
        => _ = StageAsync(TreeView.ContextRows, (files, rows) => files.RestoreAsync(rows));

    /// <summary>Delete stages the selection: dropped files are left out, the entry's own files are removed.</summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
        {
            return;
        }

        _ = StageAsync(TreeView.SelectedRows, (files, rows) => files.StageDeletionAsync(rows));
        e.Handled = true;
    }

    /// <summary>
    /// The preview is rebuilt on the thread pool, so this returns to the window at
    /// once and only picks up where the user was when the new rows are there. The
    /// staging methods never throw, which is what lets the handlers drop the task.
    /// </summary>
    private async Task StageAsync(
        IReadOnlyList<FileTreeRow> rows, Func<InstanceFilesViewModel, IReadOnlyList<FileTreeRow>, Task> stage)
    {
        if (rows.Count == 0 || Files is not { IsBusy: false } files)
        {
            return;
        }

        var anchor = rows[0].Path;
        await stage(files, rows);
        TreeView.RevealPath(anchor);
    }

    // The menu hangs off the tree, whose DataContext is the tree view model, so the
    // dialog command is reached from here with the row the user right-clicked.
    private void OnRevealInStorageClick(object? sender, RoutedEventArgs e)
        => Files?.RevealInStorageCommand.Execute(TreeView.ContextRow);
}
