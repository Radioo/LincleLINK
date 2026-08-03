using System.Text.Json;
using FluentAssertions;

namespace LincleLINK.UITests;

/// <summary>Settings page: theme, worker threads, data location, legacy import.</summary>
public sealed class SettingsTests : UITestBase
{
    [UIFact]
    public void ThemeChange_IsPersistedToSettings()
    {
        using var app = AppSession.Launch(seedSettings: true, theme: "Light");
        Run(app, () =>
        {
            app.WaitForId("NavSettings").Click();

            var light = app.WaitForId("ThemeLight");
            light.Selected.Should().BeTrue("the seeded settings use the Light theme");

            app.WaitForId("ThemeDark").Click();
            app.WaitUntil(() => app.WaitForId("ThemeDark").Selected, "Dark radio selected");

            app.WaitUntil(
                () => File.Exists(app.SettingsFile) && File.ReadAllText(app.SettingsFile).Contains("\"Dark\""),
                "theme change saved to the settings file");
        });
    }

    [UIFact]
    public void ThreadSlider_Change_IsPersistedToSettings()
    {
        using var app = AppSession.Launch(seedSettings: true, threads: 1);
        Run(app, () =>
        {
            app.WaitForId("NavSettings").Click();

            app.WaitForId("ThreadCountText").Text.Should().Be("1");

            // Click the track (jumps the thumb), then nudge right; both paths end
            // above the seeded minimum on any machine with more than one core.
            var slider = app.WaitForId("ThreadSlider");
            slider.Click();
            app.SendGlobalKeys(OpenQA.Selenium.Keys.Right);

            app.WaitUntil(() => StoredThreadCount(app) > 1, "thread count saved");
            app.WaitForId("ThreadCountText").Text.Should().NotBe("1");
        });
    }

    [UIFact]
    public void ChangeDataDirectory_PersistsChoice_AndWarnsAboutRestart()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("NavSettings").Click();
            app.WaitForId("ChangeDataDir").Click();

            var newDir = Path.Combine(app.TempRoot, "new-data-root");
            app.CompleteFolderPicker("Select data directory", newDir);

            // The explicit restart notice, then the inline pending note.
            app.WaitForText("Restart required");
            app.ClickMessageButton("OK");
            app.WaitForId("RestartPendingNote");

            // Persisted for next launch; the settings page shows the new path.
            app.WaitUntil(
                () => string.Equals(
                    StoredDataDirectory(app), Path.GetFullPath(newDir), StringComparison.OrdinalIgnoreCase),
                "new data directory saved");
            Path.GetFullPath(app.WaitForId("SettingsDataDirectory").Text)
                .Should().BeEquivalentTo(Path.GetFullPath(newDir));

            // The running session keeps using the old storage location.
            Directory.Exists(Path.Combine(app.DataDirectory, "db")).Should().BeTrue();
        });
    }

    [UIFact]
    public void ImportLegacy_CancellingPicker_LogsCancellation()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("NavSettings").Click();
            app.WaitForId("ImportLegacy").Click();

            app.DismissDialogWithEscape("Select legacy DBInfo.xml");

            app.WaitForId("ActivityLogToggle").Click();
            app.WaitForText("Import cancelled.");
        });
    }

    private static int StoredThreadCount(AppSession app)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(app.SettingsFile));
        return settings.RootElement.GetProperty("HashThreadCount").GetInt32();
    }

    private static string StoredDataDirectory(AppSession app)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(app.SettingsFile));
        return Path.GetFullPath(settings.RootElement.GetProperty("DataDirectory").GetString()!);
    }
}
