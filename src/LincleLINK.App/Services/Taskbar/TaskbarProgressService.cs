using LincleLINK.App.Abstractions;

namespace LincleLINK.App.Services.Taskbar;

/// <summary>
/// Platform-neutral half of the taskbar integration: throttles progress to
/// whole-percent changes (operations report per file, which can be thousands of
/// callbacks), tracks the begin/report/end lifecycle, and shields callers from
/// backend faults so a broken shell integration can never break an operation.
/// </summary>
public sealed class TaskbarProgressService : ITaskbarProgress
{
    private readonly ITaskbarProgressBackend _backend;
    private readonly Func<bool> _isWindowActive;

    private bool _operationRunning;
    private int _lastWholePercent = -1;

    /// <param name="backend">The OS-specific adapter.</param>
    /// <param name="isWindowActive">
    /// Returns whether the main window currently has focus; completion
    /// attention is only requested when it does not.
    /// </param>
    public TaskbarProgressService(ITaskbarProgressBackend backend, Func<bool> isWindowActive)
    {
        _backend = backend;
        _isWindowActive = isWindowActive;
    }

    public void BeginOperation()
    {
        _operationRunning = true;
        _lastWholePercent = -1;
        Try(static b => b.SetIndeterminate());
    }

    public void Report(double percent)
    {
        if (!_operationRunning)
        {
            return;
        }

        var whole = (int)Math.Clamp(percent, 0, 100);
        if (whole == _lastWholePercent)
        {
            return;
        }

        _lastWholePercent = whole;
        Try(b => b.SetValue(whole));
    }

    public void EndOperation()
    {
        if (!_operationRunning)
        {
            return;
        }

        _operationRunning = false;
        _lastWholePercent = -1;
        Try(static b => b.Clear());

        if (!IsWindowActive())
        {
            Try(static b => b.RequestAttention());
        }
    }

    private bool IsWindowActive()
    {
        try
        {
            return _isWindowActive();
        }
        catch
        {
            // If focus state cannot be determined, err on the quiet side.
            return true;
        }
    }

    private void Try(Action<ITaskbarProgressBackend> action)
    {
        try
        {
            action(_backend);
        }
        catch
        {
            // The shell indicator is best-effort decoration; never let a COM,
            // D-Bus or Objective-C failure escape into the operation itself.
        }
    }
}
