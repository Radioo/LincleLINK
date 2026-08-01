namespace LincleLINK.App;

/// <summary>
/// Creates an <see cref="IProgress{T}"/> that marshals to the captured UI
/// SynchronizationContext when one exists (production), and invokes the handler
/// synchronously otherwise (deterministic headless tests).
/// </summary>
public static class ProgressBridge
{
    public static IProgress<T> Create<T>(Action<T> handler)
    {
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
}
