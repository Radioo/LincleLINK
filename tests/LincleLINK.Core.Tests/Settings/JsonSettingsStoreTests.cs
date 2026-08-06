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
        settings.Theme.Should().Be(AppTheme.System);
        settings.DataDirectory.Should().BeNull();
        settings.HashThreadCount.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(AppTheme.Dark, Path.Combine(_temp.Root, "data"), 2));

        var reloaded = new JsonSettingsStore(SettingsPath);
        reloaded.Exists.Should().BeTrue();
        var settings = reloaded.Load();
        settings.Theme.Should().Be(AppTheme.Dark);
        settings.DataDirectory.Should().Be(Path.Combine(_temp.Root, "data"));
        settings.HashThreadCount.Should().Be(2);
    }

    [Fact]
    public void ViewMode_roundtrips_through_save_and_load()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(AppTheme.System, null, 2, LibraryViewMode.Grid));

        new JsonSettingsStore(SettingsPath).Load().ViewMode.Should().Be(LibraryViewMode.Grid);
    }

    [Fact]
    public void Missing_ViewMode_defaults_to_list()
    {
        // A settings file written before the ViewMode field existed (or with it
        // absent) must come back as List, never as Grid.
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"IsDarkTheme": false, "DataDirectory": null, "HashThreadCount": 2}""");

        new JsonSettingsStore(SettingsPath).Load().ViewMode.Should().Be(LibraryViewMode.List);
    }

    [Fact]
    public void SaveLogToFile_roundtrips_through_save_and_load()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(AppTheme.Light, null, 2, SaveLogToFile: true));

        new JsonSettingsStore(SettingsPath).Load().SaveLogToFile.Should().BeTrue();
    }

    [Fact]
    public void SaveLogToFile_defaults_to_false_when_not_persisted()
    {
        // A settings file written before the SaveLogToFile field existed must come
        // back as false (file logging is opt-in, never silently enabled).
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"Theme": "System", "DataDirectory": null, "HashThreadCount": 2}""");

        new JsonSettingsStore(SettingsPath).Load().SaveLogToFile.Should().BeFalse();
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var store = new JsonSettingsStore(SettingsPath);
        store.Save(new AppSettings(AppTheme.Light, null, 1));
        File.Exists(SettingsPath).Should().BeTrue();
    }

    [Fact]
    public void Corrupt_file_returns_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ not json");

        var store = new JsonSettingsStore(SettingsPath);
        var settings = store.Load();
        settings.Theme.Should().Be(AppTheme.System);
        settings.DataDirectory.Should().BeNull();
        settings.HashThreadCount.Should().Be(Environment.ProcessorCount);
    }

    [Theory]
    [InlineData("true", AppTheme.Dark)]
    [InlineData("false", AppTheme.Light)]
    public void Legacy_IsDarkTheme_bool_migrates_to_theme(string isDark, AppTheme expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, $$"""{"IsDarkTheme": {{isDark}}, "DataDirectory": null, "HashThreadCount": 2}""");

        new JsonSettingsStore(SettingsPath).Load().Theme.Should().Be(expected);
    }

    [Fact]
    public void Theme_value_wins_over_legacy_IsDarkTheme()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"Theme": "System", "IsDarkTheme": true, "DataDirectory": null, "HashThreadCount": 2}""");

        new JsonSettingsStore(SettingsPath).Load().Theme.Should().Be(AppTheme.System);
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
