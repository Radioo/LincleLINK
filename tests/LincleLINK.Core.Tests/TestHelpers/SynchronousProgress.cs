namespace LincleLINK.Core.Tests.TestHelpers;

/// <summary>IProgress that invokes its callback inline (deterministic for tests).</summary>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _callback;

    public SynchronousProgress(Action<T> callback)
    {
        _callback = callback;
    }

    public void Report(T value) => _callback(value);
}
