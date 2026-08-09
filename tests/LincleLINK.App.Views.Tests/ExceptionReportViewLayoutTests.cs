using Avalonia.Controls;
using FluentAssertions;
using LincleLINK.App.ViewModels;
using LincleLINK.App.Views;
using Xunit;

namespace LincleLINK.App.Views.Tests;

/// <summary>
/// Layout regression test for the report window (issue #16 D2): the stack-trace
/// text box must grow with the window when it is resized vertically, not stay
/// pinned to its content height (Semi's theme centers TextBoxes by default).
/// </summary>
public sealed class ExceptionReportViewLayoutTests
{
    [Fact]
    public void Trace_area_fills_vertical_growth()
    {
        HeadlessAppHost.RunOnUiThread(() =>
        {
            var shortTrace = MeasureTraceHeight(480);
            var tallTrace = MeasureTraceHeight(640);

            tallTrace.Should().BeGreaterThan(shortTrace + 60,
                $"the trace should grow with the window (short={shortTrace}, tall={tallTrace})");
        });
    }

    private static double MeasureTraceHeight(double windowHeight)
    {
        var vm = new ExceptionReportViewModel(
            new InvalidOperationException("boom"), isFatal: false, () => { });
        var view = new ExceptionReportView { DataContext = vm };

        var window = new Window { Content = view, Width = 620, Height = windowHeight };
        window.Show();
        var trace = view.FindControl<TextBox>("DetailsBox")!.Bounds.Height;
        window.Close();
        return trace;
    }
}
