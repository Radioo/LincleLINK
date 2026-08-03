using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LincleLINK.Core.Abstractions.Linking;

namespace LincleLINK.Core.Infrastructure.Linking;

/// <summary>
/// Hard linker for Unix-like targets via libc link(2). The mapped errno values
/// (EPERM/ENOENT/EEXIST/EXDEV/EMLINK) are identical on Linux and macOS.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class UnixHardLinker : IHardLinker
{
    public bool TryCreateLink(string sourcePath, string linkPath, out string? error)
    {
        if (link(sourcePath, linkPath) == 0)
        {
            error = null;
            return true;
        }

        var errno = Marshal.GetLastPInvokeError();
        error = errno switch
        {
            1 => "Operation not permitted.",
            2 => "Could not find the source file in storage.",
            17 => "A file with that name already exists at the target.",
            18 => "The folder is on a different filesystem than storage - hard links only work within one filesystem.",
            31 => "Too many hard links for this file.",
            _ => $"Could not create the hard link (errno {errno}).",
        };
        return false;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);
}
