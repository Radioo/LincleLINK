using System.Text.Json;
using System.Text.Json.Serialization;
using LincleLINK.Core.Abstractions.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LincleLINK.Core.Infrastructure.Settings;

/// <summary>
/// Settings stored in a JSON file at the injected path (the per-OS config dir is
/// decided by the app bootstrapper). Missing/corrupt files yield defaults; saves
/// are atomic and best-effort.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsFile;
    private readonly ILogger<JsonSettingsStore> _logger;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// On-disk shape, tolerant of older files: v2/early-v3 stored a
    /// <c>IsDarkTheme</c> bool instead of the <c>Theme</c> enum, and files written
    /// before issue #17 have no <c>SaveLogToFile</c> (loads as false).
    /// </summary>
    private sealed record PersistedSettings(
        AppTheme? Theme,
        bool? IsDarkTheme,
        string? DataDirectory,
        int? HashThreadCount,
        bool? SaveLogToFile);

    public JsonSettingsStore(string settingsFile)
        : this(settingsFile, NullLogger<JsonSettingsStore>.Instance)
    {
    }

    public JsonSettingsStore(string settingsFile, ILogger<JsonSettingsStore> logger)
    {
        _settingsFile = settingsFile;
        _logger = logger;
    }

    public bool Exists => File.Exists(_settingsFile);

    public AppSettings Load()
    {
        if (!Exists)
        {
            return Defaults();
        }

        try
        {
            var json = File.ReadAllText(_settingsFile);
            var persisted = JsonSerializer.Deserialize<PersistedSettings>(json, Options);
            if (persisted is null)
            {
                return Defaults();
            }

            return Normalize(new AppSettings(
                persisted.Theme ?? persisted.IsDarkTheme switch
                {
                    // Migrate the legacy bool: an existing explicit choice keeps its
                    // look; only files without any theme value fall back to System.
                    true => AppTheme.Dark,
                    false => AppTheme.Light,
                    null => AppTheme.System,
                },
                persisted.DataDirectory,
                persisted.HashThreadCount ?? Environment.ProcessorCount,
                persisted.SaveLogToFile ?? false));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt settings reset to defaults; surface so a silently reset config
            // cannot go unnoticed (same visibility as a failed save).
            _logger.LogWarning(e, "Failed to load settings from {SettingsFile}", _settingsFile);
            return Defaults();
        }
    }

    private static AppSettings Defaults() => new(AppTheme.System, null, Environment.ProcessorCount);

    private static AppSettings Normalize(AppSettings settings)
        => settings with
        {
            HashThreadCount = Math.Clamp(settings.HashThreadCount, 1, Environment.ProcessorCount),
        };

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = _settingsFile + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, Options));
            File.Move(tempPath, _settingsFile, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort, like v2 - but surface the failure so it is not invisible.
            _logger.LogWarning(e, "Failed to save settings to {SettingsFile}", _settingsFile);
        }
    }
}
