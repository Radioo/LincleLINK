using FluentAssertions;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Settings;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// Edge cases of the legacy v2 <c>settings.json</c> theme parsing and the
/// <c>CompleteFirstLaunch</c> theme fallback (the "no theme file" path).
/// </summary>
public sealed class FirstLaunchLegacyThemeTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => Path.Combine(_temp.Root, "config", "settings.json");

    /// <summary>
    /// Points CWD at a temp dir containing legacy v2 data (db/ + instance/) plus the
    /// given settings.json content, then resolves the data directory so the legacy
    /// theme parser runs against it. Returns the resolved result.
    /// </summary>
    private FirstLaunchResult ResolveWithLegacyCwd(string? settingsJson)
    {
        var cwd = Path.Combine(_temp.Root, "cwd");
        Directory.CreateDirectory(Path.Combine(cwd, "db"));
        Directory.CreateDirectory(Path.Combine(cwd, "instance"));
        if (settingsJson is not null)
        {
            File.WriteAllText(Path.Combine(cwd, "settings.json"), settingsJson);
        }

        var original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = cwd;
            return new FirstLaunchService(new JsonSettingsStore(SettingsPath), NullLogger<FirstLaunchService>.Instance).ResolveDataDirectory();
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void Legacy_theme_with_IsDark_key_maps_to_dark()
    {
        var result = ResolveWithLegacyCwd("""{"IsDark": true}""");

        result.LegacyDarkTheme.Should().BeTrue();
    }

    [Fact]
    public void Legacy_settings_without_theme_flags_is_light()
    {
        var result = ResolveWithLegacyCwd("""{"SomeOther": "value"}""");

        result.LegacyDarkTheme.Should().BeFalse();
    }

    [Fact]
    public void Legacy_settings_with_non_object_root_is_light()
    {
        var result = ResolveWithLegacyCwd("[1, 2, 3]");

        result.LegacyDarkTheme.Should().BeFalse();
    }

    [Fact]
    public void Corrupt_legacy_settings_yields_null_theme()
    {
        var result = ResolveWithLegacyCwd("{ this is not json");

        result.LegacyDarkTheme.Should().BeNull();
    }

    [Fact]
    public void CompleteFirstLaunch_without_legacy_theme_keeps_configured_theme()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(AppTheme.Light, null, 2));
        var service = new FirstLaunchService(store, NullLogger<FirstLaunchService>.Instance);

        // No settings.json in the chosen data directory -> ReadLegacyDarkTheme is
        // null -> the persisted Theme is preserved (not forced to Dark/Light).
        var dataDir = Path.Combine(_temp.Root, "data");
        Directory.CreateDirectory(dataDir);

        service.CompleteFirstLaunch(dataDir);

        store.Load().Theme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void CompleteFirstLaunch_with_legacy_light_theme_persists_light()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var service = new FirstLaunchService(store, NullLogger<FirstLaunchService>.Instance);
        var dataDir = Path.Combine(_temp.Root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), """{"IsDarkTheme": false}""");

        service.CompleteFirstLaunch(dataDir);

        store.Load().Theme.Should().Be(AppTheme.Light);
    }
}
