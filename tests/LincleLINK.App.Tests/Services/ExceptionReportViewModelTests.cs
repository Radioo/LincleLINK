using FluentAssertions;
using LincleLINK.App.Services;
using LincleLINK.App.ViewModels;
using Xunit;

namespace LincleLINK.App.Tests;

/// <summary>
/// Report-window view model coverage (issue #16 D2): recoverable vs fatal mode,
/// the occurrence badge, the overflow line, and the assembled copy report.
/// </summary>
public sealed class ExceptionReportViewModelTests
{
    [Fact]
    public void Recoverable_mode_shows_continue_and_recoverable_copy()
    {
        var vm = Create(false);

        vm.IsFatal.Should().BeFalse();
        vm.Title.Should().Be("Unexpected error - LincleLINK");
        vm.Headline.Should().Be("LincleLINK ran into an unexpected error");
        vm.Helper.Should().Contain("You can keep using the app");
        vm.NotFatal.Should().BeTrue();
        vm.QuitIsDefault.Should().BeFalse();
    }

    [Fact]
    public void Fatal_mode_hides_continue_and_makes_quit_the_default()
    {
        var vm = Create(true);

        vm.IsFatal.Should().BeTrue();
        vm.Title.Should().Be("LincleLINK could not start");
        vm.Headline.Should().Be("LincleLINK has to close");
        vm.Helper.Should().Contain("An error stopped the app from starting");
        vm.NotFatal.Should().BeFalse();
        vm.QuitIsDefault.Should().BeTrue();
    }

    [Fact]
    public void ContinueCommand_raises_CloseRequested()
    {
        var vm = Create(false);
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.ContinueCommand.Execute(null);

        closed.Should().BeTrue();
    }

    [Fact]
    public void MakeFatal_flips_presentation_to_fatal()
    {
        var vm = Create(false);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.MakeFatal();

        vm.IsFatal.Should().BeTrue();
        vm.Title.Should().Be("LincleLINK could not start");
        vm.Headline.Should().Be("LincleLINK has to close");
        vm.Helper.Should().Contain("An error stopped the app from starting");
        vm.NotFatal.Should().BeFalse();
        vm.QuitIsDefault.Should().BeTrue();
        changed.Should().Contain(nameof(vm.Headline));
        changed.Should().Contain(nameof(vm.Helper));
        changed.Should().Contain(nameof(vm.NotFatal));
        changed.Should().Contain(nameof(vm.QuitIsDefault));
    }

    [Fact]
    public void MakeFatal_is_a_no_op_when_already_fatal()
    {
        var vm = Create(true);

        vm.MakeFatal();

        vm.IsFatal.Should().BeTrue();
        vm.Title.Should().Be("LincleLINK could not start");
    }

    [Fact]
    public void Summary_carries_type_and_message()
    {
        var vm = Create(false, new InvalidOperationException("boom"));

        vm.SummaryType.Should().Be("System.InvalidOperationException");
        vm.SummaryMessage.Should().Be("boom");
        vm.OccurrencesText.Should().BeEmpty();
    }

    [Fact]
    public void Repeated_primary_exception_drives_the_occurrence_badge()
    {
        var vm = Create(false, new InvalidOperationException("boom"));

        vm.AddException(new InvalidOperationException("boom"));
        vm.AddException(new InvalidOperationException("boom"));

        vm.OccurrencesText.Should().Be("×3");
    }

    [Fact]
    public void Different_exceptions_append_sections_and_overflow()
    {
        var vm = Create(false, new InvalidOperationException("first"));

        for (var i = 0; i < ExceptionAccumulator.MaxDistinctExceptions + 1; i++)
        {
            vm.AddException(new InvalidOperationException($"error {i}"));
        }

        vm.DetailsText.Should().Contain("─ Also occurred ─");
        vm.OverflowText.Should().Be("…and 2 more distinct errors");
    }

    [Fact]
    public void BuildReportText_contains_report_and_extra_sections()
    {
        var vm = Create(false, new InvalidOperationException("boom"));
        vm.AddException(new NullReferenceException("second"));

        var report = vm.BuildReportText();

        report.Should().Contain("### Crash report");
        report.Should().Contain("System.InvalidOperationException: boom");
        report.Should().Contain("### Also occurred");
        report.Should().Contain("System.NullReferenceException: second");
    }

    [Fact]
    public void First_overflowed_exception_raises_overflow_property_changed()
    {
        var vm = Create(false, new InvalidOperationException("first"));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Fill to the 10-exception cap: the initial one + 9 more.
        for (var i = 0; i < ExceptionAccumulator.MaxDistinctExceptions - 1; i++)
        {
            vm.AddException(new InvalidOperationException($"error {i}"));
        }

        vm.AddException(new InvalidOperationException("over the cap"));

        changed.Should().Contain(nameof(vm.OverflowText));
        changed.Should().Contain(nameof(vm.DetailsText));
        vm.OverflowText.Should().Be("…and 1 more distinct errors");
    }

    private static ExceptionReportViewModel Create(bool isFatal, Exception? exception = null)
        => new(exception ?? new InvalidOperationException("boom"), isFatal, () => { });
}
