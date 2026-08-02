namespace LincleLINK.Core.Application;

/// <summary>
/// Drives a percent progress counter over a fixed-size collection. Centralizes the
/// zero-division guard and index bookkeeping that was repeated at every progress
/// reporting site.
/// </summary>
public readonly struct ProgressStep
{
    private readonly double _step;

    private ProgressStep(int total)
    {
        _step = total == 0 ? 0 : 100d / total;
    }

    public static ProgressStep Over(int total) => new(total);

    /// <summary>Increments the step counter and returns the cumulative percent (0 when the total is 0).</summary>
    public double Report(ref int index) => ++index * _step;
}
