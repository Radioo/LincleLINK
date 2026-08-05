using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LincleLINK.Core.Abstractions.Disk;

namespace LincleLINK.Core.Infrastructure.Disk;

/// <summary>
/// Linux free-space provider using statvfs, which is more reliable than DriveInfo
/// on unusual mounts. Struct only declares the leading fields we read.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class UnixStatFsDriveInfoProvider : IDriveInfoProvider
{
    private readonly Func<string, StatVfs> _statvfs;

    public UnixStatFsDriveInfoProvider() : this(StatvfsNative)
    {
    }

    /// <summary>Test seam: injects the native statvfs call so the P/Invoke is not required on a non-Linux host.</summary>
    internal UnixStatFsDriveInfoProvider(Func<string, StatVfs> statvfs)
    {
        _statvfs = statvfs;
    }

    public long GetAvailableFreeSpace(string path)
    {
        var info = _statvfs(path);
        return checked((long)(info.f_bavail * info.f_frsize));
    }

    private static StatVfs StatvfsNative(string path)
    {
        if (statvfs(path, out var info) != 0)
        {
            throw new InvalidOperationException($"statvfs failed for '{path}'.");
        }

        return info;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StatVfs
    {
        public ulong f_bsize;
        public ulong f_frsize;
        public ulong f_blocks;
        public ulong f_bfree;
        public ulong f_bavail;
        public ulong f_files;
        public ulong f_ffree;
        public ulong f_favail;
        public ulong f_fsid;
        public ulong f_flag;
        public ulong f_namemax;
        private ulong f_spare0; // glibc: int __f_spare[6] (24 bytes) after f_namemax
        private ulong f_spare1;
        private ulong f_spare2;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "statvfs")]
    private static extern int statvfs(string path, out StatVfs buf);
}
