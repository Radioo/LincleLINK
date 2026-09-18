using System.Collections.Concurrent;

namespace LincleLINK.Core.Tests.TestHelpers;

/// <summary>
/// Stands in for the UI thread in tests of the rule that the UI never freezes
/// (CLAUDE.md). Work started through <see cref="RunAsync{T}"/> begins with this
/// context current, the way a command handler begins on the UI thread, and every
/// continuation that comes back to the context runs with it current again. A port
/// that finds the context current was therefore called on the "UI thread".
/// </summary>
public sealed class UiThreadStandIn : SynchronizationContext
{
    private readonly ConcurrentQueue<string> _onUiThread = new();

    /// <summary>What was noted while the context was current, i.e. what ran on the "UI thread".</summary>
    public IReadOnlyCollection<string> RanOnUiThread => _onUiThread;

    public static bool IsCurrent => Current is UiThreadStandIn;

    /// <summary>Call from a fake port: records <paramref name="what"/> when it runs on the "UI thread".</summary>
    public void Note(string what)
    {
        if (IsCurrent)
        {
            _onUiThread.Enqueue(what);
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            d(state);
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    /// <summary>Starts <paramref name="work"/> on the "UI thread" and awaits it from outside.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        Task<T> running;
        try
        {
            running = work();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }

        return await running.ConfigureAwait(false);
    }
}
