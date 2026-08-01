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

    public long GetTotalSize(string path)
    {
        var drive = ResolveDrive(path);
        return drive.TotalSize;
    }

    private static DriveInfo ResolveDrive(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.GetPathRoot(Environment.CurrentDirectory);
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
