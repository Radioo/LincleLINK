using System.Diagnostics;

namespace LincleLINK.App.Services;

/// <summary>
/// Opens a folder in the platform file manager (issue #17 D2, "Open log folder").
/// </summary>
public static class FolderOpener
{
    public static void Open(string path)
        => Open(path, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    /// <summary>
    /// Builds the launch info for the platform's file manager. Parameterized by OS
    /// flags so the selection logic is unit-testable on any host.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string path, bool isWindows, bool isMacOS)
    {
        var fileName = isWindows ? "explorer.exe" : isMacOS ? "open" : "xdg-open";
        var info = new ProcessStartInfo(fileName) { UseShellExecute = true };
        info.ArgumentList.Add(path);
        return info;
    }

    private static void Open(string path, bool isWindows, bool isMacOS)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        using var process = Process.Start(CreateStartInfo(path, isWindows, isMacOS));
    }
}
