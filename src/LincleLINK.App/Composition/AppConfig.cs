namespace LincleLINK.App.Composition;

/// <summary>
/// Well-known filesystem locations in the per-OS config directory (issue #17 D1).
/// The log folder lives next to <c>settings.json</c>: it must be known before
/// settings resolve and must survive a broken or unplugged data volume.
/// </summary>
public static class AppConfig
{
    private static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LincleLINK");

    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(ConfigDirectory, "logs");
}
