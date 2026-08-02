using FluentAssertions;
using LincleLINK.App.Services;
using Xunit;

namespace LincleLINK.App.Tests.Services;

public sealed class BatchedQueueTests
{
    /// <summary>A post that queues actions for manual dispatch, simulating a dispatcher.</summary>
    private sealed class ManualDispatcher
    {
        public List<Action> Pending { get; } = [];

        public void Post(Action action) => Pending.Add(action);

        public void RunOne()
        {
            var action = Pending[0];
            Pending.RemoveAt(0);
            action();
        }
    }

    private static BatchedQueue<T> CreateWith<T>(ManualDispatcher dispatcher, Action<T> handler, int batchSize)
        => new(dispatcher.Post, handler, batchSize);

    [Fact]
    public void Report_SchedulesOneDrain_ThenFlushesBatchSizeItems()
    {
        var dispatcher = new ManualDispatcher();
        var delivered = new List<int>();
        var queue = CreateWith<int>(dispatcher, delivered.Add, batchSize: 3);

        for (var i = 1; i <= 3; i++)
        {
            queue.Report(i);
        }

        // Only one drain is scheduled while pending, despite three reports.
        dispatcher.Pending.Should().HaveCount(1);

        dispatcher.RunOne();
        delivered.Should().BeEquivalentTo([1, 2, 3], options => options.WithStrictOrdering());
        dispatcher.Pending.Should().BeEmpty();
    }

    [Fact]
    public void Drain_LeavesRemainder_AndReschedules()
    {
        var dispatcher = new ManualDispatcher();
        var delivered = new List<int>();
        var queue = CreateWith<int>(dispatcher, delivered.Add, batchSize: 2);

        for (var i = 1; i <= 5; i++)
        {
            queue.Report(i);
        }

        dispatcher.Pending.Should().HaveCount(1);

        dispatcher.RunOne();
        delivered.Should().BeEquivalentTo([1, 2], options => options.WithStrictOrdering());

        // Items remain, so a new drain is scheduled rather than stalling.
        dispatcher.Pending.Should().HaveCount(1);

        dispatcher.RunOne();
        delivered.Should().BeEquivalentTo([1, 2, 3, 4], options => options.WithStrictOrdering());

        dispatcher.RunOne();
        delivered.Should().BeEquivalentTo([1, 2, 3, 4, 5], options => options.WithStrictOrdering());
        dispatcher.Pending.Should().BeEmpty();
    }

    [Fact]
    public void Drain_IsNotScheduledWhileAlreadyPending()
    {
        var dispatcher = new ManualDispatcher();
        var queue = CreateWith<int>(dispatcher, _ => { }, batchSize: 1);

        for (var i = 0; i < 5; i++)
        {
            queue.Report(i);
        }

        dispatcher.Pending.Should().HaveCount(1);
    }

    [Fact]
    public void EmptyQueue_AfterDrain_SchedulesNothing()
    {
        var dispatcher = new ManualDispatcher();
        var delivered = new List<int>();
        var queue = CreateWith<int>(dispatcher, delivered.Add, batchSize: 2);

        queue.Report(1);
        dispatcher.RunOne();
        delivered.Should().BeEquivalentTo([1]);
        dispatcher.Pending.Should().BeEmpty();

        queue.Report(2);
        dispatcher.RunOne();
        delivered.Should().BeEquivalentTo([1, 2]);
        dispatcher.Pending.Should().BeEmpty();
    }
}
