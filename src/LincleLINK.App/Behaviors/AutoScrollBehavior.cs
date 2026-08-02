using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace LincleLINK.App.Behaviors;

/// <summary>
/// Pins a virtualizing <see cref="ListBox"/> to its last item when new lines are
/// added, but only when the view is already at/near the bottom — so the user can
/// scroll up and keep reading without being dragged back down. It wires the
/// ListBox's internal ScrollViewer (the one virtualization reports its extent
/// through) and calls <see cref="ScrollViewer.ScrollToEnd"/> on extent growth.
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly AttachedProperty<bool> AutoScrollToEndProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>(
            "AutoScrollToEnd",
            typeof(AutoScrollBehavior));

    public static void SetAutoScrollToEnd(ListBox element, bool value)
        => element.SetValue(AutoScrollToEndProperty, value);

    public static bool GetAutoScrollToEnd(ListBox element)
        => element.GetValue(AutoScrollToEndProperty);

    private static readonly ConditionalWeakTable<ListBox, ScrollViewer> s_wired = new();

    static AutoScrollBehavior()
    {
        AutoScrollToEndProperty.Changed.AddClassHandler<ListBox>(OnChanged);
    }

    private static void OnChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            listBox.LayoutUpdated += OnLayoutUpdated;
        }
        else
        {
            listBox.LayoutUpdated -= OnLayoutUpdated;
            if (s_wired.TryGetValue(listBox, out var scrollViewer))
            {
                scrollViewer.ScrollChanged -= OnScrollChanged;
                s_wired.Remove(listBox);
            }
        }
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is not ListBox listBox
            || listBox.Scroll is not ScrollViewer scrollViewer)
        {
            return;
        }

        // Wire (or re-wire after a template/theme change) to the internal ScrollViewer.
        if (!s_wired.TryGetValue(listBox, out var wired) || wired != scrollViewer)
        {
            if (wired is not null)
            {
                wired.ScrollChanged -= OnScrollChanged;
            }

            scrollViewer.ScrollChanged += OnScrollChanged;
            s_wired.Remove(listBox);
            s_wired.Add(listBox, scrollViewer);
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || e.ExtentDelta.Y <= 0)
        {
            return;
        }

        // Extent already includes the growth, so compare against the pre-growth extent.
        // Only pin to the bottom when the user was already at/near it.
        double wasAtBottom = scrollViewer.Extent.Height - e.ExtentDelta.Y - 1;
        if (scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= wasAtBottom)
        {
            scrollViewer.ScrollToEnd();
        }
    }
}
