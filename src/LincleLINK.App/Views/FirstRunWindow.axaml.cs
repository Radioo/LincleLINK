using Avalonia;
using Avalonia.Controls;

namespace LincleLINK.App.Views;

public partial class FirstRunWindow : UserControl
{
    public FirstRunWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ViewModels.FirstRunViewModel vm)
        {
            vm.Confirmed += (_, _) => (VisualRoot as Window)?.Close();
        }
    }
}
