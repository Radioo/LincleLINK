using FluentAssertions;
using LincleLINK.App.Services.Taskbar;
using NSubstitute;
using Xunit;

namespace LincleLINK.App.Tests.Services;

public sealed class TaskbarProgressServiceTests
{
    private readonly ITaskbarProgressBackend _backend = Substitute.For<ITaskbarProgressBackend>();
    private bool _windowActive = true;

    private TaskbarProgressService CreateService()
        => new(_backend, () => _windowActive);

    [Fact]
    public void BeginOperation_shows_indeterminate_until_first_report()
    {
        var service = CreateService();

        service.BeginOperation();

        _backend.Received(1).SetIndeterminate();
        _backend.DidNotReceive().SetValue(Arg.Any<double>());
    }

    [Fact]
    public void Report_forwards_value_after_begin()
    {
        var service = CreateService();
        service.BeginOperation();

        service.Report(42.4);

        _backend.Received(1).SetValue(42);
    }

    [Fact]
    public void Report_outside_operation_is_ignored()
    {
        var service = CreateService();

        service.Report(50);

        _backend.DidNotReceive().SetValue(Arg.Any<double>());
    }

    [Fact]
    public void Report_throttles_to_whole_percent_changes()
    {
        var service = CreateService();
        service.BeginOperation();

        service.Report(10.1);
        service.Report(10.4);
        service.Report(10.9);
        service.Report(11.0);

        _backend.Received(1).SetValue(10);
        _backend.Received(1).SetValue(11);
        _backend.Received(2).SetValue(Arg.Any<double>());
    }

    [Fact]
    public void Report_clamps_out_of_range_values()
    {
        var service = CreateService();
        service.BeginOperation();

        service.Report(-5);
        service.Report(250);

        _backend.Received(1).SetValue(0);
        _backend.Received(1).SetValue(100);
    }

    [Fact]
    public void EndOperation_clears_indicator()
    {
        var service = CreateService();
        service.BeginOperation();

        service.EndOperation();

        _backend.Received(1).Clear();
    }

    [Fact]
    public void EndOperation_requests_attention_only_when_window_inactive()
    {
        var service = CreateService();

        service.BeginOperation();
        service.EndOperation();
        _backend.DidNotReceive().RequestAttention();

        _windowActive = false;
        service.BeginOperation();
        service.EndOperation();
        _backend.Received(1).RequestAttention();
    }

    [Fact]
    public void EndOperation_without_begin_does_nothing()
    {
        var service = CreateService();

        service.EndOperation();

        _backend.DidNotReceive().Clear();
        _backend.DidNotReceive().RequestAttention();
    }

    [Fact]
    public void New_operation_reports_same_percent_again()
    {
        var service = CreateService();

        service.BeginOperation();
        service.Report(50);
        service.EndOperation();

        service.BeginOperation();
        service.Report(50);

        _backend.Received(2).SetValue(50);
    }

    [Fact]
    public void Backend_failures_never_escape_to_the_caller()
    {
        _backend.When(b => b.SetIndeterminate()).Throw<InvalidOperationException>();
        _backend.When(b => b.SetValue(Arg.Any<double>())).Throw<InvalidOperationException>();
        _backend.When(b => b.Clear()).Throw<InvalidOperationException>();
        _backend.When(b => b.RequestAttention()).Throw<InvalidOperationException>();
        _windowActive = false;
        var service = CreateService();

        var act = () =>
        {
            service.BeginOperation();
            service.Report(10);
            service.EndOperation();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Failing_window_state_probe_suppresses_attention()
    {
        var service = new TaskbarProgressService(_backend, () => throw new InvalidOperationException());

        service.BeginOperation();
        var act = service.EndOperation;

        act.Should().NotThrow();
        _backend.DidNotReceive().RequestAttention();
    }
}
