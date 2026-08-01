using System.Text.Json;
using LincleLINK.Core.Abstractions.Settings;

namespace LincleLINK.Core.Application;

public enum FirstLaunchAction
{
    UseExistingSettings,
    AdoptCurrentDirectory,
    PromptForDirectory,
}

public sealed record FirstLaunchResult(
    FirstLaunchAction Action,
    string DataDirectory,
    bool HasLegacyV2Data,
    bool? LegacyDarkTheme);

/// <summary>
/// Resolves the data directory at startup and adopts legacy v2 data non-destructively.
/// UI-free: when <see cref="FirstLaunchAction.PromptForDirectory"/> is returned, the
/// app shows a picker and then calls <see cref="CompleteFirstLaunch"/>.
/// </summary>
public sealed class FirstLaunchService
{
    private readonly ISettingsStore _settingsStore;

    public FirstLaunchService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public FirstLaunchResult Resolve()
    {
        var settings = _settingsStore.Load();

        if (_settingsStore.Exists)
        {
            var dir = settings.DataDirectory ?? Environment.CurrentDirectory;
            return new FirstLaunchResult(FirstLaunchAction.UseExistingSettings, dir, false, null);
        }

        var cwd = Environment.CurrentDirectory;
        var hasV2 = HasLegacyData(cwd);

        if (hasV2)
        {
            return new FirstLaunchResult(FirstLaunchAction.AdoptCurrentDirectory, cwd, true, ReadLegacyDarkTheme(cwd));
        }

        return new FirstLaunchResult(FirstLaunchAction.PromptForDirectory, cwd, false, ReadLegacyDarkTheme(cwd));
    }

    /// <summary>
    /// Persists the chosen data directory and any legacy dark-theme preference so
    /// the next launch is not treated as first launch. Safe to call more than once.
    /// </summary>
    public void CompleteFirstLaunch(string dataDirectory)
    {
        var settings = _settingsStore.Load();
        var isDark = ReadLegacyDarkTheme(dataDirectory) ?? settings.IsDarkTheme;
        _settingsStore.Save(new AppSettings(isDark, dataDirectory));
    }

    private static bool HasLegacyData(string dir)
        => Directory.Exists(Path.Combine(dir, "db")) && Directory.Exists(Path.Combine(dir, "instance"));

    private static bool? ReadLegacyDarkTheme(string dir)
    {
        var path = Path.Combine(dir, "settings.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("IsDarkTheme", out var el) && el.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (root.TryGetProperty("IsDark", out var el2) && el2.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            return false;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
