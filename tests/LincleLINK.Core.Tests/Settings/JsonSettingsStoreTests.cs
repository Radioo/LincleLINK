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
        settings.HashThreadCount.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(true, Path.Combine(_temp.Root, "data"), 2));

        var reloaded = new JsonSettingsStore(SettingsPath);
        reloaded.Exists.Should().BeTrue();
        var settings = reloaded.Load();
        settings.IsDarkTheme.Should().BeTrue();
        settings.DataDirectory.Should().Be(Path.Combine(_temp.Root, "data"));
        settings.HashThreadCount.Should().Be(2);
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(false, null, 1));
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
        settings.HashThreadCount.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Out_of_range_hash_thread_count_is_clamped()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"IsDarkTheme": false, "DataDirectory": null, "HashThreadCount": 9999}""");

        var store = new JsonSettingsStore(SettingsPath);
        store.Load().HashThreadCount.Should().Be(Environment.ProcessorCount);

        File.WriteAllText(SettingsPath, """{"IsDarkTheme": false, "DataDirectory": null, "HashThreadCount": 0}""");
        store.Load().HashThreadCount.Should().Be(1);
    }
}
