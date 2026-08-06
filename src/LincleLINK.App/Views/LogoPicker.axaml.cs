using Avalonia.Controls;
using Avalonia.Input;
using LincleLINK.App.Logos;
using LincleLINK.App.ViewModels;

namespace LincleLINK.App.Views;

public partial class LogoPicker : UserControl
{
    public LogoPicker()
    {
        InitializeComponent();
    }

    private void OnLogoClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is LogoEntry logo)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SetCustomLogoCommand.Execute(logo);
            }
        }
    }
}
