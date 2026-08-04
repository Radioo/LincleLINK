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
}
