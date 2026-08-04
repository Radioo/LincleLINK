using System.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Infrastructure.Collections;

namespace LincleLINK.App.Views;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();

        // Column-click sorting on the Name column should use natural order
        // ("IIDX 9th style" before "IIDX 10th style"), not the default
        // culture-aware lexical comparison.
        var nameColumn = LibraryGrid.Columns.FirstOrDefault(c => c.SortMemberPath == "InstanceName");
        if (nameColumn is not null)
        {
            nameColumn.CustomSortComparer = NaturalCellComparer.Instance;
        }
    }

    private void OnGridItemClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is InstanceListEntry entry)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedInstance = entry;
            }
        }
    }

    /// <summary>
    /// Clicking the logo column header restores the default supported-list order:
    /// it clears the DataGrid's active sort so the grid falls back to the
    /// ItemsSource order (which the view model keeps in catalog order).
    /// </summary>
    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        // The logo column is the only template column, so it identifies the
        // header click that should reset the sort.
        if (e.Column is DataGridTemplateColumn)
        {
            e.Handled = true;
            LibraryGrid.CollectionView.SortDescriptions.Clear();
        }
    }
}

/// <summary>
/// Cell-value comparer for the DataGrid's user-driven column sort: string cells
/// compare with <see cref="NaturalStringComparer"/>, everything else with the
/// default comparer.
/// </summary>
internal sealed class NaturalCellComparer : IComparer
{
    public static readonly NaturalCellComparer Instance = new();

    private NaturalCellComparer()
    {
    }

    public int Compare(object? x, object? y)
    {
        if (x is string xs && y is string ys)
        {
            return NaturalStringComparer.Instance.Compare(xs, ys);
        }

        return Comparer.Default.Compare(x, y);
    }
}
