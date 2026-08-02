using System.Runtime.Versioning;
using LincleLINK.Core.Abstractions.Disk;

namespace LincleLINK.Core.Infrastructure.Disk;

/// <summary>
/// macOS free-space provider. DriveInfo stats the given path directly on Unix
/// (no mount enumeration), and the runtime shim uses Darwin statfs with 64-bit
/// block counts — unlike Darwin statvfs, whose 32-bit counts overflow on large
/// volumes and whose layout differs from the Linux struct used by
/// <see cref="UnixStatFsDriveInfoProvider"/>.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacDriveInfoProvider : IDriveInfoProvider
{
    public long GetAvailableFreeSpace(string path)
    {
        return new DriveInfo(Path.GetFullPath(path)).AvailableFreeSpace;
    }
}
