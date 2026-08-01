using FluentAssertions;
using LincleLINK.Core.Abstractions.Settings;
using LincleLINK.Core.Infrastructure.Settings;
using LincleLINK.Core.Tests.TestHelpers;
using Xunit;

namespace LincleLINK.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => Path.Combine(_temp.Root, "config", "settings.json");

    [Fact]
    public void Missing_file_returns_defaults_and_Exists_false()
    {
        var store = new JsonSettingsStore(SettingsPath);

        store.Exists.Should().BeFalse();
        var settings = store.Load();
        settings.IsDarkTheme.Should().BeFalse();
        settings.DataDirectory.Should().BeNull();
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(true, Path.Combine(_temp.Root, "data")));

        var reloaded = new JsonSettingsStore(SettingsPath);
        reloaded.Exists.Should().BeTrue();
        var settings = reloaded.Load();
        settings.IsDarkTheme.Should().BeTrue();
        settings.DataDirectory.Should().Be(Path.Combine(_temp.Root, "data"));
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(false, null));
        File.Exists(SettingsPath).Should().BeTrue();
    }

    [Fact]
    public void Corrupt_file_returns_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        var store = new JsonSettingsStore(SettingsPath);
        var settings = store.Load();
        settings.IsDarkTheme.Should().BeFalse();
        settings.DataDirectory.Should().BeNull();
    }
}
