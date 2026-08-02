namespace LincleLINK.App.Abstractions;

/// <summary>
/// Mirrors in-app operation progress onto the OS shell (Windows taskbar button,
/// Linux dock/launcher entry, macOS Dock icon) and raises a passive
/// needs-attention notification when an operation finishes while the app is in
/// the background. All members are safe to call unconditionally: failures in
/// the shell integration never surface to callers.
/// </summary>
public interface ITaskbarProgress
{
    /// <summary>
    /// Marks the start of an operation. Shows an indeterminate indicator until
    /// the first <see cref="Report"/> call switches it to a concrete value.
    /// </summary>
    void BeginOperation();

    /// <summary>Reports operation progress in percent (0..100).</summary>
    void Report(double percent);

    /// <summary>
    /// Marks the end of an operation: clears the shell indicator and, if the
    /// window is not active, asks the shell for passive attention (taskbar
    /// flash / dock bounce) so the user notices completion.
    /// </summary>
    void EndOperation();
}
