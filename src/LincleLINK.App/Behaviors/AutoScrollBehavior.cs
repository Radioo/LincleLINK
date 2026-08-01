using Avalonia;
using Avalonia.Controls;

namespace LincleLINK.App.Behaviors;

/// <summary>
/// Scrolls a ScrollViewer to the end when new content is added, but only if it was
/// already at the bottom — so the user can scroll up and keep reading old lines
/// without being dragged to the bottom.
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
        if (sender is not ScrollViewer viewer || e.ExtentDelta.Y <= 0)
        {
            return;
        }

        // Extent already includes the growth, so compare against the pre-growth extent.
        // Only pin to the bottom when the user was already at/near it.
        double wasAtBottom = viewer.Extent.Height - e.ExtentDelta.Y - 1;
        if (viewer.Offset.Y + viewer.Viewport.Height >= wasAtBottom)
        {
            viewer.ScrollToEnd();
        }
    }
}
