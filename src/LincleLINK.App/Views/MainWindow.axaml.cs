using Avalonia.Controls;
using Avalonia.Input;
using LincleLINK.App.Logos;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using LincleLINK.Core.Domain;

namespace LincleLINK.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ThemeManager.ApplyTitleBar(this);

        if (DataContext is MainViewModel viewModel)
        {
            _ = viewModel.InitializeAsync();
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

    private void OnCloseLogoPicker(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SetCustomLogoCommand.Execute(null);
        }
    }
}
