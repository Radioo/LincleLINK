namespace LincleLINK.App.Abstractions;

/// <summary>
/// Channels handed to a running operation (plan 14 D5): log lines mirrored into
/// the diagnostic log, percent progress, and the operation's cancellation token
/// (wired to the shell's Cancel button).
/// </summary>
public sealed record OperationContext(
    IProgress<string> Log,
    IProgress<double> Percent,
    CancellationToken CancellationToken);

/// <summary>
/// Shared operation host for child view models that need the main window's busy
/// gating and progress scaffolding without holding a reference to the concrete
/// <c>MainViewModel</c>. Implemented by the main VM and consumed by feature VMs
/// (e.g. the torrent-check tab).
/// </summary>
public interface IOperationHost
{
    /// <summary>True while any operation is running (gates commands across tabs).</summary>
    bool IsBusy { get; }

    /// <summary>
    /// Runs an operation on the thread pool, marshaling log/status/progress to the UI.
    /// <paramref name="operationName"/> is a short human-readable tag used as the
    /// diagnostic-log scope and in start/duration/outcome events (issue #17 D4).
    /// </summary>
    Task RunOperationAsync(string operationName, Func<OperationContext, Task> operation);
}
