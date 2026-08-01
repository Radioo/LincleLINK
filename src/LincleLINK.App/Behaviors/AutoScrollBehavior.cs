using Avalonia;
using Avalonia.Controls;

namespace LincleLINK.App.Behaviors;

/// <summary>
/// Scrolls a ScrollViewer to the end whenever its content grows (new log lines),
/// without fighting the user's manual scrolling.
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly AttachedProperty<bool> AutoScrollToEndProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "AutoScrollToEnd",
            typeof(AutoScrollBehavior));

    public static void SetAutoScrollToEnd(ScrollViewer element, bool value)
        => element.SetValue(AutoScrollToEndProperty, value);

    public static bool GetAutoScrollToEnd(ScrollViewer element)
        => element.GetValue(AutoScrollToEndProperty);

    static AutoScrollBehavior()
    {
        AutoScrollToEndProperty.Changed.AddClassHandler<ScrollViewer>(OnChanged);
    }

    private static void OnChanged(ScrollViewer viewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            viewer.ScrollChanged += OnScrollChanged;
        }
        else
        {
            viewer.ScrollChanged -= OnScrollChanged;
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer && e.ExtentDelta.Y > 0)
        {
            viewer.ScrollToEnd();
        }
    }
}
