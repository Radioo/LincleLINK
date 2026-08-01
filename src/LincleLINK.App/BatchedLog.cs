using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Threading;

namespace LincleLINK.App;

/// <summary>
/// Creates an <see cref="IProgress{T}"/> for high-frequency text logs. In production
/// (Avalonia is initialized) reports are queued on the producing threads and drained
/// to the handler in bounded batches via the UI dispatcher at Background priority, so
/// thousands of per-file lines never flood the UI thread in a single burst and input/
/// rendering keep priority. Headless tests (no Avalonia) get synchronous delivery so
/// VM assertions stay deterministic.
/// </summary>
public static class BatchedLog
{
    public static IProgress<T> Create<T>(Action<T> handler, int batchSize = 100)
    {
        if (Application.Current is null)
        {
            return new SyncLog<T>(handler);
        }

        return new TimedLog<T>(handler, batchSize);
    }

    private sealed class SyncLog<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class TimedLog<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        private readonly int _batchSize;
        private readonly ConcurrentQueue<T> _queue = new();
        private readonly object _gate = new();
        private bool _drainScheduled;

        public TimedLog(Action<T> handler, int batchSize)
        {
            _handler = handler;
            _batchSize = batchSize;
        }

        public void Report(T value)
        {
            _queue.Enqueue(value);
            ScheduleDrain();
        }

        private void ScheduleDrain()
        {
            var post = false;
            lock (_gate)
            {
                if (!_drainScheduled)
                {
                    _drainScheduled = true;
                    post = true;
                }
            }

            if (post)
            {
                Dispatcher.UIThread.Post(Drain, DispatcherPriority.Background);
            }
        }

        private void Drain()
        {
            lock (_gate)
            {
                _drainScheduled = false;
            }

            var flushed = 0;
            while (flushed < _batchSize && _queue.TryDequeue(out var value))
            {
                _handler(value);
                flushed++;
            }

            if (!_queue.IsEmpty)
            {
                ScheduleDrain();
            }
        }
    }
}
