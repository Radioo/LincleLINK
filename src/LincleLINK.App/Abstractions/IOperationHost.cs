using System.Collections.ObjectModel;

namespace LincleLINK.App.Abstractions;

/// <summary>
/// Channels handed to a running operation (plan 14 D5): durable log lines,
/// a transient latest-wins status line, percent progress, and the operation's
/// cancellation token (wired to the shell's Cancel button).
/// </summary>
public sealed record OperationContext(
    IProgress<string> Log,
    IProgress<string> Status,
    IProgress<double> Percent,
    CancellationToken CancellationToken);

/// <summary>
/// Shared operation host for child view models that need the main window's busy
/// gating, log lines, and progress scaffolding without holding a reference to the
/// concrete <c>MainViewModel</c>. Implemented by the main VM and consumed by
/// feature VMs (e.g. the torrent-check tab).
/// </summary>
public interface IOperationHost
{
    /// <summary>True while any operation is running (gates commands across tabs).</summary>
    bool IsBusy { get; }

    /// <summary>The shared log panel lines collection.</summary>
    ObservableCollection<string> LogLines { get; }

    /// <summary>Runs an operation on the thread pool, marshaling log/status/progress to the UI.</summary>
    Task RunOperationAsync(Func<OperationContext, Task> operation);
}
