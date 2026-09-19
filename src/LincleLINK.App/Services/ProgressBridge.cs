using Avalonia;
using Avalonia.Threading;

namespace LincleLINK.App.Services;

/// <summary>
/// Creates an <see cref="IProgress{T}"/> that marshals to the UI thread. When
/// <paramref name="batchSize"/> &gt; 1 (high-frequency text logs) reports are queued
/// on the producing threads and drained in bounded batches via the UI dispatcher at
/// Background priority, so thousands of per-file lines never flood the UI thread in
/// a single burst and input/rendering keep priority. Headless tests (no Avalonia,
/// no SynchronizationContext) get synchronous delivery so VM assertions stay
/// deterministic. The batching/drain mechanics live in <see cref="BatchedQueue{T}"/>
/// so they can be unit-tested without a dispatcher.
/// </summary>
public static class ProgressBridge
{
    public static IProgress<T> Create<T>(Action<T> handler, int batchSize = 1)
    {
        if (batchSize > 1 && Application.Current is not null)
        {
            return new TimedProgress<T>(handler, batchSize);
        }

        if (SynchronizationContext.Current is not null)
        {
            return new Progress<T>(handler);
        }

        return new SyncProgress<T>(handler);
    }

    /// <summary>
    /// A percent channel to the UI thread that lets through only what moves a
    /// progress bar: a change of a tenth of a percent, a restart, and the final 100.
    /// A deploy links 150k files in seconds, and one dispatcher post per file would
    /// keep the UI thread busy with nothing but progress updates (CLAUDE.md: the UI
    /// never freezes).
    /// </summary>
    public static IProgress<double> CreatePercent(Action<double> handler)
        => new ThinnedPercent(Create(handler));

    private sealed class ThinnedPercent(IProgress<double> inner) : IProgress<double>
    {
        private readonly Lock _gate = new();
        private double _last = double.NaN;

        public void Report(double value)
        {
            lock (_gate)
            {
                var moves = double.IsNaN(_last)
                    || Math.Abs(value - _last) >= 0.1
                    || (value >= 100 && _last < 100);
                if (!moves)
                {
                    return;
                }

                _last = value;
            }

            inner.Report(value);
        }
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class TimedProgress<T> : IProgress<T>
    {
        private readonly BatchedQueue<T> _queue;

        public TimedProgress(Action<T> handler, int batchSize)
        {
            _queue = new BatchedQueue<T>(
                action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background),
                handler,
                batchSize);
        }

        public void Report(T value) => _queue.Report(value);
    }
}
