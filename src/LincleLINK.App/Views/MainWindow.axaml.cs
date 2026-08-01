using Avalonia.Controls;
using LincleLINK.App.Services;

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
    }
}
