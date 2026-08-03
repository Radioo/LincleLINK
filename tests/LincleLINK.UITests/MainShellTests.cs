using FluentAssertions;

namespace LincleLINK.UITests;

public sealed class MainShellTests : UITestBase
{
    [UIFact]
    public void SeededSettings_SkipFirstRun_AndShowEmptyLibrary()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            app.WaitForId("AddFirstFolder");

            // Startup created the storage layout inside the seeded data directory.
            app.WaitUntil(
                () => Directory.Exists(Path.Combine(app.DataDirectory, "db")),
                "db directory created in the data root");
        });
    }

    [UIFact]
    public void Navigation_SwitchesBetweenPages()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");

            app.WaitForId("NavSettings").Click();
            app.WaitForId("SettingsPageHeader");
            var dataDirectoryBox = app.WaitForId("SettingsDataDirectory");
            Path.GetFullPath(dataDirectoryBox.Text).Should().BeEquivalentTo(Path.GetFullPath(app.DataDirectory));

            app.WaitForId("NavTorrent").Click();
            app.WaitForId("TorrentPageHeader");

            app.WaitForId("NavLibrary").Click();
            app.WaitForId("LibraryEmptyState");
        });
    }

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
            app.WaitForId("ThemeDark").Selected.Should().BeTrue();

            app.WaitUntil(
                () => File.Exists(app.SettingsFile) && File.ReadAllText(app.SettingsFile).Contains("\"Dark\""),
                "theme change saved to the settings file");
        });
    }
}
