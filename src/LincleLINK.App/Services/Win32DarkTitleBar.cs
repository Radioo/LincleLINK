using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace LincleLINK.App.Services;

/// <summary>
/// Darkens the native Windows title bar (v2 ThemeManager.ApplyImmersiveTitleBar).
/// No-op / harmless elsewhere; only invoked on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Win32DarkTitleBar
{
    public static void Apply(Window window, bool dark)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        var attr = 20; // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 1903+)
        if (DwmSetWindowAttribute(handle, attr, ref value, sizeof(int)) != 0)
        {
            attr = 19; // pre-1903 fallback
            DwmSetWindowAttribute(handle, attr, ref value, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
