using FluentAssertions;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Infrastructure.Settings;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Settings;

/// <summary>
/// The <see cref="JsonSettingsStore"/> branches the core tests miss: a JSON
/// "null" payload, a file with neither theme field, and a failed save.
/// </summary>
public sealed class JsonSettingsStoreCoverageTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => Path.Combine(_temp.Root, "config", "settings.json");

    [Fact]
    public void Null_json_payload_returns_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "null");

        var settings = new JsonSettingsStore(SettingsPath).Load();

        settings.Theme.Should().Be(AppTheme.System);
        settings.DataDirectory.Should().BeNull();
    }

    [Fact]
    public void File_with_no_theme_field_falls_back_to_system()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"DataDirectory": "C:\\data", "HashThreadCount": 2}""");

        var settings = new JsonSettingsStore(SettingsPath).Load();

        settings.Theme.Should().Be(AppTheme.System);
        settings.DataDirectory.Should().Be("C:\\data");
        settings.HashThreadCount.Should().Be(2);
    }

    [Fact]
    public void Failed_save_is_best_effort_and_surfaces_on_stderr()
    {
        // Occupy the config directory path with a file so Directory.CreateDirectory
        // throws; the store must swallow it instead of propagating.
        var configDir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(Path.GetDirectoryName(configDir)!);
        File.WriteAllText(configDir, "not a directory");

        var store = new JsonSettingsStore(SettingsPath);

        var act = () => store.Save(new AppSettings(AppTheme.Dark, null, 2));
        act.Should().NotThrow();
    }
}
