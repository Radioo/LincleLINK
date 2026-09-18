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

    /// <summary>Shows a file in the platform file manager, selected where the platform can do that.</summary>
    public static void Reveal(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        using var process = Process.Start(
            CreateRevealStartInfo(filePath, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()));
    }

    /// <summary>
    /// Launch info for revealing a file. xdg-open cannot select a file, so other
    /// platforms open the containing folder instead.
    /// </summary>
    public static ProcessStartInfo CreateRevealStartInfo(string filePath, bool isWindows, bool isMacOS)
    {
        if (isWindows)
        {
            // Explorer parses "/select," itself and needs the quotes around the
            // path only, which ArgumentList would place around the whole switch.
            return new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = $"/select,\"{filePath}\"",
            };
        }

        if (isMacOS)
        {
            var info = new ProcessStartInfo("open") { UseShellExecute = true };
            info.ArgumentList.Add("-R");
            info.ArgumentList.Add(filePath);
            return info;
        }

        return CreateStartInfo(Path.GetDirectoryName(filePath) ?? filePath, isWindows, isMacOS);
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
