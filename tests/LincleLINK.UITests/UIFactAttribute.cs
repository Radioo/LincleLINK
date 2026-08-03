using Xunit;

namespace LincleLINK.UITests;

/// <summary>
/// A fact that only runs when UI testing is explicitly enabled. The suite needs
/// a Windows desktop session and a running WinAppDriver (Appium's Windows
/// driver), so a plain <c>dotnet test</c> on the solution skips these instead
/// of failing on machines without that infrastructure.
/// </summary>
public sealed class UIFactAttribute : FactAttribute
{
    public UIFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "LincleLINK UI tests drive the app through WinAppDriver and run only on Windows.";
        }
        else if (Environment.GetEnvironmentVariable("LINCLELINK_UI_TESTS") != "1")
        {
            Skip = "Set LINCLELINK_UI_TESTS=1 (with WinAppDriver running) to run the Appium UI tests.";
        }
    }
}
