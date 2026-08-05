using Avalonia.Controls;
using Avalonia.Input;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Domain;

namespace LincleLINK.App.Views;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
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
    /// ItemsSource order (which the view model keeps in catalog order). The Name
    /// column keeps its plain alphabetical sort - that is intentional.
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
