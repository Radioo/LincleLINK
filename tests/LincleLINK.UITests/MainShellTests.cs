using FluentAssertions;

namespace LincleLINK.UITests;

/// <summary>Shell chrome: navigation, storage card, activity bar and log drawer.</summary>
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
    public void ActivityLogDrawer_TogglesAndShowsLogLines()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");

            app.WaitForId("ActivityLogToggle").Click();
            app.WaitForId("ActivityLogList");

            // The startup refresh leaves a line in the log.
            app.WaitForText("Library refreshed.");

            app.WaitForId("ActivityLogToggle").Click();
            app.WaitForGoneById("ActivityLogList");
        });
    }

    [UIFact]
    public void StorageCard_ShowsFigures()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");

            app.WaitUntil(
                () => app.WaitForId("StorageSavings").Text.StartsWith("Saving", StringComparison.Ordinal),
                "storage card savings line populated");
        });
    }
}
