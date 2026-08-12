using FluentAssertions;
using LincleLINK.App.Services;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class ExceptionReportTests
{
    private static readonly DateTimeOffset FixedUtc =
        new(2026, 8, 5, 14, 3, 12, TimeSpan.Zero);

    [Fact]
    public void Format_includes_type_message_and_stack()
    {
        var text = ExceptionReport.Format(CreateException(), FixedUtc, 1);

        text.Should().Contain("### Crash report");
        text.Should().Contain("System.InvalidOperationException: boom");
        text.Should().Contain("ExceptionReportTests.CreateException");
    }

    [Fact]
    public void Format_renders_inner_exceptions()
    {
        var text = ExceptionReport.Format(CreateException(), FixedUtc, 1);

        text.Should().Contain("System.IO.IOException: inner");
    }

    [Fact]
    public void Format_includes_environment_lines()
    {
        var text = ExceptionReport.Format(new Exception("x"), FixedUtc, 1);

        text.Should().MatchRegex(@"- \*\*Version:\*\* .+");
        text.Should().MatchRegex(@"- \*\*OS:\*\* .+");
        text.Should().Contain("- **Runtime:** .NET");
        text.Should().Contain("· Avalonia ");
    }

    [Fact]
    public void Format_writes_the_injected_timestamp_and_occurrences()
    {
        var text = ExceptionReport.Format(new Exception("x"), FixedUtc, 7);

        text.Should().Contain("- **When (UTC):** 2026-08-05 14:03:12");
        text.Should().Contain("- **Occurrences:** 7");
    }

    [Fact]
    public void Format_is_deterministic_for_a_given_timestamp()
    {
        var first = ExceptionReport.Format(new Exception("x"), FixedUtc, 1);
        var second = ExceptionReport.Format(new Exception("x"), FixedUtc, 1);

        first.Should().Be(second);
    }

    [Fact]
    public void Format_balances_code_fences()
    {
        var text = ExceptionReport.Format(CreateException(), FixedUtc, 1);

        text.Count(c => c == '`').Should().Be(6);
        text.Should().Contain("```");
        text.TrimEnd().Should().EndWith("```");
    }

    [Fact]
    public void Format_keeps_the_repro_prompt_placeholder()
    {
        var text = ExceptionReport.Format(new Exception("x"), FixedUtc, 1);

        text.Should().Contain("**What I was doing:**");
    }

    [Fact]
    public void EnvironmentLine_reports_app_os_runtime_and_avalonia()
    {
        ExceptionReport.EnvironmentLine.Should().StartWith("LincleLINK ");
        ExceptionReport.EnvironmentLine.Should().Contain("· Avalonia ");
        ExceptionReport.EnvironmentLine.Should().Contain(".NET");
    }

    private static Exception CreateException()
    {
        try
        {
            throw new InvalidOperationException("boom", new IOException("inner"));
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
