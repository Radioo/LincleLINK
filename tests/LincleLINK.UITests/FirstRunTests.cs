using System.Text.Json;
using FluentAssertions;

namespace LincleLINK.UITests;

public sealed class FirstRunTests : UITestBase
{
    [UIFact]
    public void FirstRun_AcceptingDefaultDirectory_LandsInEmptyLibrary()
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
            using var settings = JsonDocument.Parse(File.ReadAllText(app.SettingsFile));
            var storedDirectory = settings.RootElement.GetProperty("DataDirectory").GetString();
            Path.GetFullPath(storedDirectory!).Should().BeEquivalentTo(Path.GetFullPath(app.DataDirectory));
        });
    }
}
