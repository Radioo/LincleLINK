namespace LincleLINK.App.Abstractions;

/// <summary>
/// Channels handed to a running operation (plan 14 D5): log lines mirrored into
/// the diagnostic log, percent progress, and the operation's cancellation token
/// (wired to the shell's Cancel button).
/// </summary>
public sealed record OperationContext(
    IProgress<string> Log,
    IProgress<double> Percent,
    CancellationToken CancellationToken)
{
    /// <summary>
    /// What the operation is doing right now, shown next to the shell's progress
    /// bar and not kept: per-file lines belong here, outcomes in <see cref="Log"/>.
    /// Log lines show there too, so an operation that only logs is never silent.
    /// </summary>
    public IProgress<string> Status { get; init; } = Log;
}

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
    /// Runs an operation with the shell busy, showing its name, status and progress
    /// in the activity bar. The operation starts on the calling (UI) thread, because
    /// it may open dialogs; the services it calls move their own work to the thread
    /// pool (CLAUDE.md: the UI never freezes). <paramref name="operationName"/> is a
    /// short human-readable tag, also used as the diagnostic-log scope and in
    /// start/duration/outcome events (issue #17 D4).
    /// </summary>
    Task RunOperationAsync(string operationName, Func<OperationContext, Task> operation);
}
