using Avalonia.Controls;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;

namespace LincleLINK.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        InitializeDevMenu();
#endif
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

#if DEBUG
    /// <summary>
    /// DEBUG-only crash triggers (issue #16 D6): throw on demand from each
    /// exception surface so the global handler can be exercised without UI
    /// automation. Compiled out of Release builds entirely.
    /// </summary>
    private void InitializeDevMenu()
    {
        var debug = new MenuItem { Header = "Debug" };
        debug.Items.Add(CreateThrowItem(
            "Throw on the UI thread",
            () => Throw(new InvalidOperationException("Deliberate UI-thread exception (dev menu)"))));
        debug.Items.Add(CreateThrowItem(
            "Throw in an un-awaited task",
            () => _ = Task.Run(() => Throw(new InvalidOperationException("Deliberate unobserved task exception (dev menu)")))));
        debug.Items.Add(CreateThrowItem(
            "Throw on a raw background thread",
            () =>
            {
                var thread = new Thread(() => Throw(new InvalidOperationException("Deliberate background-thread exception (dev menu)")));
                thread.Start();
            }));
        debug.Items.Add(CreateThrowItem(
            "Throw inside an operation",
            () =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    // Exercises the RunOperationAsync catch-all routing (D5).
                    _ = viewModel.RunOperationAsync("Dev throw", async _ =>
                        throw new InvalidOperationException("Deliberate operation exception (dev menu)"));
                }
            }));

        var menu = new Menu();
        menu.Items.Add(debug);
        DevMenuHost.Children.Add(menu);
    }

    private static MenuItem CreateThrowItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static void Throw(Exception exception) => throw exception;
#endif
}
