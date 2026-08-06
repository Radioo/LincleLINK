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

    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}
