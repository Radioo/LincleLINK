using Avalonia.Controls;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;

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

        // Refresh after the window is shown and the dispatcher is pumping, so the
        // initial instance list and status reach the (lazily realized) tab content.
        if (DataContext is MainViewModel viewModel)
        {
            _ = viewModel.InitializeAsync();
        }
    }
}
