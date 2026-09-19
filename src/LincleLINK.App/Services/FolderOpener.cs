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

    /// <summary>
    /// Shows a file in the platform file manager, selected where the platform can do
    /// that. A file that is not there opens its folder instead, so the caller's menu
    /// item never does nothing. Touches the disk: call it off the UI thread.
    /// </summary>
    public static void Reveal(string filePath)
    {
        var folder = Path.GetDirectoryName(filePath);
        var info = ChooseRevealStartInfo(
            filePath,
            File.Exists(filePath),
            !string.IsNullOrEmpty(folder) && Directory.Exists(folder),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS());
        if (info is null)
        {
            return;
        }

        using var process = Process.Start(info);
    }

    /// <summary>
    /// What <see cref="Reveal"/> launches: the file selected, its folder when the file
    /// is missing, nothing when the folder is missing too. Takes what is on disk as
    /// flags so the choice is unit-testable.
    /// </summary>
    public static ProcessStartInfo? ChooseRevealStartInfo(
        string filePath, bool fileExists, bool folderExists, bool isWindows, bool isMacOS)
    {
        if (fileExists)
        {
            return CreateRevealStartInfo(filePath, isWindows, isMacOS);
        }

        return folderExists && Path.GetDirectoryName(filePath) is { Length: > 0 } folder
            ? CreateStartInfo(folder, isWindows, isMacOS)
            : null;
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
