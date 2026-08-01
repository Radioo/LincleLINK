using FluentAssertions;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Settings;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

public sealed class FirstLaunchServiceTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => Path.Combine(_temp.Root, "config", "settings.json");

    [Fact]
    public void Resolve_with_existing_settings_uses_configured_directory()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var dataDir = Path.Combine(_temp.Root, "data");
        store.Save(new AppSettings(false, dataDir, 4));

        var result = new FirstLaunchService(store).Resolve();

        result.Action.Should().Be(FirstLaunchAction.UseExistingSettings);
        result.DataDirectory.Should().Be(dataDir);
        result.HasLegacyV2Data.Should().BeFalse();
    }

    [Fact]
    public void Resolve_with_existing_settings_and_null_dir_uses_cwd()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(false, null, 1));

        var original = Environment.CurrentDirectory;
        try
        {
            var cwd = Path.Combine(_temp.Root, "cwd");
            Directory.CreateDirectory(cwd);
            Environment.CurrentDirectory = cwd;
            var result = new FirstLaunchService(store).Resolve();
            result.DataDirectory.Should().Be(cwd);
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void First_launch_with_v2_data_adopts_current_directory()
    {
        var cwd = Path.Combine(_temp.Root, "cwd");
        Directory.CreateDirectory(Path.Combine(cwd, "db"));
        Directory.CreateDirectory(Path.Combine(cwd, "instance"));
        File.WriteAllText(Path.Combine(cwd, "settings.json"), """{"IsDarkTheme": true}""");

        var original = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = cwd;
            var result = new FirstLaunchService(new JsonSettingsStore(SettingsPath)).Resolve();

            result.Action.Should().Be(FirstLaunchAction.AdoptCurrentDirectory);
            result.DataDirectory.Should().Be(cwd);
            result.HasLegacyV2Data.Should().BeTrue();
            result.LegacyDarkTheme.Should().BeTrue();
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void First_launch_without_v2_data_prompts_for_directory()
    {
        var original = Environment.CurrentDirectory;
        try
        {
            var cwd = Path.Combine(_temp.Root, "empty");
            Directory.CreateDirectory(cwd);
            Environment.CurrentDirectory = cwd;

            var result = new FirstLaunchService(new JsonSettingsStore(SettingsPath)).Resolve();

            result.Action.Should().Be(FirstLaunchAction.PromptForDirectory);
            result.HasLegacyV2Data.Should().BeFalse();
        }
        finally
        {
            Environment.CurrentDirectory = original;
        }
    }

    [Fact]
    public void CompleteFirstLaunch_persists_and_imports_legacy_dark_theme()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var service = new FirstLaunchService(store);
        var dataDir = Path.Combine(_temp.Root, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), """{"IsDarkTheme": true}""");

        service.CompleteFirstLaunch(dataDir);

        var settings = store.Load();
        settings.IsDarkTheme.Should().BeTrue();
        settings.DataDirectory.Should().Be(dataDir);

        // Next launch is no longer first launch.
        var result = service.Resolve();
        result.Action.Should().Be(FirstLaunchAction.UseExistingSettings);
    }
}
