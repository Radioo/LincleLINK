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
            return new AppSettings(false, null);
        }

        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings(false, null);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings(false, null);
        }
    }

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
