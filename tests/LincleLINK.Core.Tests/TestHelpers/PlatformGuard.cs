using Xunit.Sdk;

namespace LincleLINK.Core.Tests.TestHelpers;

/// <summary>
/// Skips a platform-specific test when the current OS is not a supported target,
/// so a green run always means the platform path actually executed (instead of a
/// silently-passing test that returned early).
/// </summary>
public static class PlatformGuard
{
    /// <summary>Throws <see cref="SkipException"/> unless running on Windows or Linux.</summary>
    public static void EnsureSupportedOs()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            throw SkipException.ForSkip("LincleLINK supports Windows and Linux only.");
        }
    }
}
