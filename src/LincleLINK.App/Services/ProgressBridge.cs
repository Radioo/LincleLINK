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
