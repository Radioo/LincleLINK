using Serilog.Core;
using Serilog.Events;

namespace LincleLINK.App.Services;

/// <summary>
/// Live switch for the on-disk diagnostic log (issue #17). Read by the Serilog
/// file sink's per-event condition and written by the Settings toggle (and by
/// startup seeding), so enabling/disabling takes effect immediately without a
/// pipeline rebuild or restart.
/// </summary>
public static class FileLoggingSwitch
{
    private static volatile bool _enabled;

    private static readonly LoggingLevelSwitch _levelSwitch = new(LogEventLevel.Information);

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            // Debug events (per-file hashes, activity mirror) are only worth
            // materializing when there is a sink that could receive them.
            _levelSwitch.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;
        }
    }

    /// <summary>
    /// The pipeline's minimum level gate, so the disabled path never pays for
    /// Debug events that both sinks would drop anyway.
    /// </summary>
    internal static LoggingLevelSwitch LevelSwitch => _levelSwitch;
}
