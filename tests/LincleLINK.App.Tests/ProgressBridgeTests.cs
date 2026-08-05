using FluentAssertions;
using LincleLINK.App.Services;
using Xunit;

namespace LincleLINK.App.Tests;

public sealed class ProgressBridgeTests
{
    [Fact]
    public void Create_without_app_or_context_uses_synchronous_delivery()
    {
        // Force the "no app, no SynchronizationContext" branch regardless of what
        // the test host may have installed on this thread.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var received = new List<int>();
            var progress = ProgressBridge.Create<int>(received.Add, batchSize: 1);

            progress.Report(1);
            progress.Report(2);

            received.Should().Equal(1, 2);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
