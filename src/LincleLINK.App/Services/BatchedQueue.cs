using System.Collections.Concurrent;

namespace LincleLINK.App.Services;

/// <summary>
/// Dispatcher-agnostic bounded batching queue. Reports are queued by producing
/// threads and drained through a caller-supplied <paramref name="post"/> function
/// (the UI dispatcher in the app, a synchronous delegate in tests) in batches of
/// <paramref name="batchSize"/>, so thousands of per-file lines never flood a
/// thread in a single burst. Draining is re-scheduled while items remain.
/// </summary>
public sealed class BatchedQueue<T>
{
    private readonly Action<Action> _post;
    private readonly Action<T> _handler;
    private readonly int _batchSize;
    private readonly ConcurrentQueue<T> _queue = new();
    private readonly object _gate = new();
    private bool _drainScheduled;

    public BatchedQueue(Action<Action> post, Action<T> handler, int batchSize)
    {
        _post = post;
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
            _post(Drain);
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
