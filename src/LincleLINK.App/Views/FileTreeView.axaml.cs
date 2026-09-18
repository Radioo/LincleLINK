using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LincleLINK.App.ViewModels.FileTree;

namespace LincleLINK.App.Views;

public partial class FileTreeView : UserControl
{
    public FileTreeView()
    {
        InitializeComponent();

        // Tunnel so the row is known before any context menu opens for it.
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// The row a context menu was requested on, or null for empty space. With
    /// several rows selected the selection can't tell which one was clicked.
    /// </summary>
    public FileTreeRow? ContextRow { get; private set; }

    /// <summary>
    /// The rows a context menu action applies to: the whole selection when the
    /// clicked row is part of it, otherwise only the clicked row.
    /// </summary>
    public IReadOnlyList<FileTreeRow> ContextRows
    {
        get
        {
            if (ContextRow is null)
            {
                return [];
            }

            var selected = SelectedRows;
            return selected.Contains(ContextRow) ? selected : [ContextRow];
        }
    }

    public IReadOnlyList<FileTreeRow> SelectedRows
        => TreeList.SelectedItems?.OfType<FileTreeRow>().ToList() ?? [];

    /// <summary>The row an event came from, e.g. the row a drop landed on; null for empty space.</summary>
    public static FileTreeRow? RowOf(object? eventSource) => (eventSource as Control)?.DataContext as FileTreeRow;

    /// <summary>
    /// Selects the row at <paramref name="path"/> and scrolls to it. Staging reloads
    /// the rows, which would otherwise leave the list at the top with no selection.
    /// </summary>
    public void RevealPath(string path)
    {
        if (Tree?.Rows.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)) is { } row)
        {
            TreeList.SelectedItem = row;
            TreeList.ScrollIntoView(row);
        }
    }

    private FileTreeViewModel? Tree => DataContext as FileTreeViewModel;

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e) => ContextRow = RowOf(e.Source);

    private void OnExpanderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileTreeRow row })
        {
            Tree?.Toggle(row);
            e.Handled = true;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowOf(e.Source) is { IsDirectory: true } row)
        {
            Tree?.Toggle(row);
        }
    }

    /// <summary>
    /// Left and Right work as in a tree: Right opens a folder and then steps into
    /// it, Left closes it and then steps out to the parent. The list handles
    /// every other key itself.
    /// </summary>
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (Tree is not { } tree
            || TreeList.SelectedItem is not FileTreeRow row
            || e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        var target = e.Key == Key.Right ? tree.StepInto(row) : tree.StepOut(row);
        if (!ReferenceEquals(target, row))
        {
            TreeList.SelectedItem = target;
            TreeList.ScrollIntoView(target);
        }

        e.Handled = true;
    }
}
