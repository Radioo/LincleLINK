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
}
