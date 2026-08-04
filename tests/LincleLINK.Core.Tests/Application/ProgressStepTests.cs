using FluentAssertions;
using LincleLINK.Core.Application;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class ProgressStepTests
{
    [Fact]
    public void Report_increments_the_index_and_scales_percent()
    {
        var step = ProgressStep.Over(4);
        int index = 0;

        step.Report(ref index).Should().Be(25);
        step.Report(ref index).Should().Be(50);
        step.Report(ref index).Should().Be(75);
        step.Report(ref index).Should().Be(100);
    }

    [Fact]
    public void Zero_total_yields_zero_percent()
    {
        var step = ProgressStep.Over(0);
        int index = 0;

        step.Report(ref index).Should().Be(0);
    }
}
