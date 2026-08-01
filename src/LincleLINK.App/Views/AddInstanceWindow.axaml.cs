using Avalonia;
using Avalonia.Controls;

namespace LincleLINK.App.Views;

public partial class AddInstanceWindow : UserControl
{
    public AddInstanceWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is ViewModels.AddInstanceViewModel vm)
        {
            vm.CloseRequested += (_, _) => (VisualRoot as Window)?.Close();
        }
    }
}
