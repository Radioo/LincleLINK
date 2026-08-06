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
    private readonly Func<string, string, int> _link;

    public UnixHardLinker() : this(LinkNative)
    {
    }

    /// <summary>Test seam: injects the libc link call so the P/Invoke is not required on a non-Unix host.</summary>
    internal UnixHardLinker(Func<string, string, int> link)
    {
        _link = link;
    }

    public bool TryCreateLink(string sourcePath, string linkPath, out string? error)
    {
        if (_link(sourcePath, linkPath) == 0)
        {
            error = null;
            return true;
        }

        error = DescribeError(Marshal.GetLastPInvokeError());
        return false;
    }

    /// <summary>Maps a libc errno to a user-presentable message (extracted for testability).</summary>
    internal static string DescribeError(int errno) => errno switch
    {
        1 => "Operation not permitted.",
        2 => "Could not find the source file in storage.",
        17 => "A file with that name already exists at the target.",
        18 => "The folder is on a different filesystem than storage - hard links only work within one filesystem.",
        31 => "Too many hard links for this file.",
        _ => $"Could not create the hard link (errno {errno}).",
    };

    private static int LinkNative(string oldpath, string newpath) => link(oldpath, newpath);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);
}
