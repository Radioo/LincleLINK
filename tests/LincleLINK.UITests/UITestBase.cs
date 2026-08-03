using System.Runtime.CompilerServices;

namespace LincleLINK.UITests;

public abstract class UITestBase
{
    /// <summary>Runs the test body and captures a screenshot + UIA tree on failure.</summary>
    protected static void Run(AppSession app, Action test, [CallerMemberName] string testName = "")
    {
        try
        {
            test();
        }
        catch
        {
            app.TryDumpArtifacts(testName);
            throw;
        }
    }

    /// <summary>
    /// Drives the slide-over add flow end to end: opens the panel (from the empty
    /// state or the header button), types name and folder path, picks the mode,
    /// confirms, and waits for the new row to appear in the library grid.
    /// </summary>
    protected static void AddEntry(AppSession app, string name, string sourceFolder, bool keepOriginals)
    {
        // The header button exists only when the grid is shown; the empty state
        // has its own button. Probe for the empty state first.
        try
        {
            app.WaitForId("AddFirstFolder", TimeSpan.FromSeconds(3)).Click();
        }
        catch (TimeoutException)
        {
            app.WaitForId("AddFolderButton").Click();
        }

        app.SetText(app.WaitForId("AddName"), name);
        app.SetText(app.WaitForId("AddFolderPath"), sourceFolder);

        if (keepOriginals)
        {
            var keep = app.WaitForId("AddModeKeep");
            keep.Click();
            app.WaitUntil(() => keep.Selected, "Keep originals selected");
        }

        app.WaitForId("AddConfirm").Click();

        // Success closes the panel and refreshes the grid.
        app.WaitForGoneById("AddName", TimeSpan.FromSeconds(60));
        app.WaitForText(name, TimeSpan.FromSeconds(30));
    }
}
