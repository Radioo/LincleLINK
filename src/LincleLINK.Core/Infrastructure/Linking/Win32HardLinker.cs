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
            2 or 3 => "Could not find the source file in the db.",
            5 => "Access denied.",
            17 => "A file with that name already exists at the target.",
            1142 => "Too many hard links for this file.",
            _ => $"Could not create the hard link (Win32 error {code}).",
        };
        return false;
    }

    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
