namespace LincleLINK.App.Abstractions;

/// <summary>
/// Routes unexpected exceptions to the global error-report surface (issue #16).
/// Implemented by <c>GlobalExceptionHandler</c>; consumed by operation hosts that
/// catch an exception before it would reach the dispatcher hooks, so the report
/// window still gets the full stack trace.
/// </summary>
public interface IExceptionReporter
{
    /// <summary>Presents an unexpected exception as a recoverable error report.</summary>
    void ReportUnexpected(Exception exception);
}
