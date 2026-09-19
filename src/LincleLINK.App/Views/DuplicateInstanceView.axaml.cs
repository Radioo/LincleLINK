using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LincleLINK.App.Views;

public partial class DuplicateInstanceView : UserControl
{
    public DuplicateInstanceView()
    {
        InitializeComponent();
    }

    /// <summary>The name is the only input, so it takes focus with its text selected, ready to be retyped.</summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        NameBox.Focus();
        NameBox.SelectAll();
    }
}
