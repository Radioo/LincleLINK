namespace LincleLINK.App.Services.Taskbar;

/// <summary>
/// Platform adapter behind <see cref="TaskbarProgressService"/>: one
/// implementation per OS shell. Implementations may throw; the service wraps
/// every call.
/// </summary>
public interface ITaskbarProgressBackend
{
    /// <summary>Shows determinate progress in percent (0..100).</summary>
    void SetValue(double percent);

    /// <summary>Shows an indeterminate (unknown-duration) indicator.</summary>
    void SetIndeterminate();

    /// <summary>Removes any progress indicator from the shell.</summary>
    void Clear();

    /// <summary>
    /// Requests passive user attention (Windows taskbar flash, dock urgency).
    /// Only called when the app window is not active.
    /// </summary>
    void RequestAttention();
}
