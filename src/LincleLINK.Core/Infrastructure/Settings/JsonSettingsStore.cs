using System.Text.Json;
using LincleLINK.Core.Abstractions.Settings;

namespace LincleLINK.Core.Infrastructure.Settings;

/// <summary>
/// Settings stored in a JSON file at the injected path (the per-OS config dir is
/// decided by the app bootstrapper). Missing/corrupt files yield defaults; saves
/// are atomic and best-effort.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _settingsFile;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonSettingsStore(string settingsFile)
    {
        _settingsFile = settingsFile;
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
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, Options) ?? Defaults());
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return Defaults();
        }
    }

    private static AppSettings Defaults() => new(false, null, Environment.ProcessorCount);

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
            // best-effort, like v2
        }
    }
}
