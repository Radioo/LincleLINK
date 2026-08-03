using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LincleLINK.Core.Abstractions.Linking;

namespace LincleLINK.Core.Infrastructure.Linking;

[SupportedOSPlatform("windows")]
public sealed class Win32HardLinker : IHardLinker
{
    public bool TryCreateLink(string sourcePath, string linkPath, out string? error)
    {
        if (CreateHardLinkW(linkPath, sourcePath, IntPtr.Zero))
        {
            error = null;
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        error = code switch
        {
            2 or 3 => "Could not find the source file in storage.",
            5 => "Access denied.",
            // 17 is ERROR_NOT_SAME_DEVICE ("cannot move the file to a different
            // disk drive"), not "already exists" - that is 80/183.
            17 => "The folder is on a different drive than storage - hard links only work within one drive.",
            80 or 183 => "A file with that name already exists at the target.",
            1142 => "Too many hard links for this file.",
            _ => $"Could not create the hard link (Win32 error {code}).",
        };
        return false;
    }

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
