using System.Runtime.Versioning;
using LincleLINK.Core.Abstractions.Disk;

namespace LincleLINK.Core.Infrastructure.Disk;

/// <summary>
/// Windows free-space provider using DriveInfo prefix matching (v2 behavior).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DriveInfoProvider : IDriveInfoProvider
{
    public long GetAvailableFreeSpace(string path)
    {
        var drive = ResolveDrive(path);
        return drive.AvailableFreeSpace;
    }

    /// <summary>Resolves the DriveInfo backing <paramref name="path"/> (extracted for testability).</summary>
    internal static DriveInfo ResolveDrive(string path)
        => ResolveDrive(path, () => Path.GetPathRoot(Environment.CurrentDirectory));

    /// <summary>
    /// Resolution core with an injectable fallback root, so the "no root" guard is
    /// unit-testable on any host (the current-directory fallback can never be null).
    /// </summary>
    internal static DriveInfo ResolveDrive(string path, Func<string?> fallbackRoot)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            root = fallbackRoot();
        }

        if (root is null)
        {
            throw new InvalidOperationException($"Could not resolve the drive for path '{path}'.");
        }

        var drive = DriveInfo.GetDrives()
            .FirstOrDefault(d => root.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase));

        if (drive is null)
        {
            throw new InvalidOperationException($"No drive found for path '{path}'.");
        }

        return drive;
    }
}
