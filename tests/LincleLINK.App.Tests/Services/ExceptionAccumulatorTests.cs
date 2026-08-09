using FluentAssertions;
using LincleLINK.App.Services;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// Storm-guard coverage (issue #16 D4): dedupe by type + message + top stack
/// frame, the distinct-exception cap and the overflow counter. Pure logic, no UI.
/// </summary>
public sealed class ExceptionAccumulatorTests
{
    [Fact]
    public void Identical_exception_increments_primary_occurrences()
    {
        var accumulator = new ExceptionAccumulator();
        var ex = new IOException("locked");

        accumulator.Add(ex);
        accumulator.Add(ex);

        accumulator.Primary.Should().BeSameAs(ex);
        accumulator.PrimaryOccurrences.Should().Be(2);
        accumulator.DistinctCount.Should().Be(1);
    }

    [Fact]
    public void Different_exceptions_accumulate_as_sections()
    {
        var accumulator = new ExceptionAccumulator();
        accumulator.Add(new IOException("first"));
        accumulator.Add(new NullReferenceException("second"));

        accumulator.PrimaryOccurrences.Should().Be(1);
        accumulator.DistinctCount.Should().Be(2);
        accumulator.AdditionalExceptions().Should().HaveCount(1);
    }

    [Fact]
    public void Cap_limits_distinct_exceptions_and_tracks_overflow()
    {
        var accumulator = new ExceptionAccumulator();
        for (var i = 0; i < ExceptionAccumulator.MaxDistinctExceptions + 3; i++)
        {
            accumulator.Add(new InvalidOperationException($"error {i}"));
        }

        accumulator.DistinctCount.Should().Be(ExceptionAccumulator.MaxDistinctExceptions);
        accumulator.OverflowCount.Should().Be(3);
        accumulator.HasOverflow.Should().BeTrue();
    }

    [Fact]
    public void Add_reports_whether_the_occurrence_was_dropped_for_the_cap()
    {
        var accumulator = new ExceptionAccumulator();
        for (var i = 0; i < ExceptionAccumulator.MaxDistinctExceptions; i++)
        {
            accumulator.Add(new InvalidOperationException($"error {i}")).Should().BeTrue();
        }

        accumulator.Add(new InvalidOperationException("over the cap")).Should().BeFalse();
    }

    [Fact]
    public void BuildDetailsText_renders_primary_trace_and_separators()
    {
        var accumulator = new ExceptionAccumulator();
        accumulator.Add(new IOException("first"));
        accumulator.Add(new NullReferenceException("second"));

        var text = accumulator.BuildDetailsText();

        text.Should().Contain("System.IO.IOException: first");
        text.Should().Contain("─ Also occurred ─");
        text.Should().Contain("System.NullReferenceException: second");
        text.Should().NotContain("more distinct errors");
    }

    [Fact]
    public void BuildDetailsText_reports_overflow_when_capped()
    {
        var accumulator = new ExceptionAccumulator();
        for (var i = 0; i < ExceptionAccumulator.MaxDistinctExceptions + 2; i++)
        {
            accumulator.Add(new InvalidOperationException($"error {i}"));
        }

        var text = accumulator.BuildDetailsText();

        text.Should().Contain("…and 2 more distinct errors");
        text.Should().Contain("10-exception cap");
    }

    [Fact]
    public void Reset_clears_items_and_overflow()
    {
        var accumulator = new ExceptionAccumulator();
        for (var i = 0; i < ExceptionAccumulator.MaxDistinctExceptions + 1; i++)
        {
            accumulator.Add(new InvalidOperationException($"error {i}"));
        }

        accumulator.Reset();

        accumulator.DistinctCount.Should().Be(0);
        accumulator.OverflowCount.Should().Be(0);
        accumulator.HasOverflow.Should().BeFalse();
        accumulator.Primary.Should().BeNull();
    }

    [Fact]
    public void BuildKey_is_stable_and_carries_type_and_message()
    {
        var thrown = Capture(() => throw new InvalidOperationException("boom"));
        var key = ExceptionAccumulator.BuildKey(thrown);

        key.Should().Be(ExceptionAccumulator.BuildKey(thrown));
        key.Should().Contain("System.InvalidOperationException");
        key.Should().Contain("boom");
        key.Should().Contain("BuildKey_is_stable_and_carries_type_and_message");
    }

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("action did not throw");
    }
}
