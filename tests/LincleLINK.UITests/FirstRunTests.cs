using System.Text.Json;
using FluentAssertions;

namespace LincleLINK.UITests;

/// <summary>First-run screen: directory prompt, validation, theme choice.</summary>
public sealed class FirstRunTests : UITestBase
{
    [UIFact]
    public void AcceptingDefaultDirectory_LandsInEmptyLibrary()
    {
        using var app = AppSession.Launch(seedSettings: false);
        Run(app, () =>
        {
            app.WaitForText("Welcome to LincleLINK");

            // The proposed directory is the app's working directory (our temp data dir).
            var directoryBox = app.WaitForId("FirstRunDataDirectory");
            Path.GetFullPath(directoryBox.Text).Should().BeEquivalentTo(Path.GetFullPath(app.DataDirectory));

            app.WaitForId("FirstRunConfirm").Click();

            app.WaitForId("LibraryEmptyState");

            // The choice is persisted to the redirected settings file.
            app.WaitUntil(() => File.Exists(app.SettingsFile), "settings file written");
            StoredDataDirectory(app).Should().BeEquivalentTo(Path.GetFullPath(app.DataDirectory));
        });
    }

    [UIFact]
    public void EmptyDirectory_ShowsValidationStatus_AndStaysOnFirstRun()
    {
        using var app = AppSession.Launch(seedSettings: false);
        Run(app, () =>
        {
            var directoryBox = app.WaitForId("FirstRunDataDirectory");
            app.SetText(directoryBox, string.Empty);

            app.WaitForId("FirstRunConfirm").Click();

            app.WaitForId("FirstRunStatus").Text.Should().Be("Choose a folder before continuing.");

            // Still on the first-run screen; nothing was persisted.
            app.WaitForId("FirstRunConfirm");
            File.Exists(app.SettingsFile).Should().BeFalse();
        });
    }

    [UIFact]
    public void TypedCustomDirectory_IsUsedAndPersisted()
    {
        using var app = AppSession.Launch(seedSettings: false);
        Run(app, () =>
        {
            // A path with a space, to exercise real text entry.
            var custom = Path.Combine(app.TempRoot, "custom storage");

            var directoryBox = app.WaitForId("FirstRunDataDirectory");
            app.SetText(directoryBox, custom);
            app.WaitForId("FirstRunConfirm").Click();

            app.WaitForId("LibraryEmptyState");

            // The app booted against the typed directory: storage layout + settings.
            app.WaitUntil(() => Directory.Exists(Path.Combine(custom, "db")), "db dir created in custom directory");
            StoredDataDirectory(app).Should().BeEquivalentTo(Path.GetFullPath(custom));
        });
    }

    [UIFact]
    public void ThemeChoice_OnFirstRun_IsPersisted()
    {
        using var app = AppSession.Launch(seedSettings: false);
        Run(app, () =>
        {
            var dark = app.WaitForId("FirstRunThemeDark");
            dark.Click();
            app.WaitUntil(() => dark.Selected, "Dark radio selected");

            app.WaitForId("FirstRunConfirm").Click();
            app.WaitForId("LibraryEmptyState");

            app.WaitUntil(
                () => File.Exists(app.SettingsFile) && File.ReadAllText(app.SettingsFile).Contains("\"Dark\""),
                "Dark theme persisted");
        });
    }

    private static string StoredDataDirectory(AppSession app)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(app.SettingsFile));
        return Path.GetFullPath(settings.RootElement.GetProperty("DataDirectory").GetString()!);
    }
}
