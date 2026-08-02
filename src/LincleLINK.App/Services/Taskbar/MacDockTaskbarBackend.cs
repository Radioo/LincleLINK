using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LincleLINK.App.Services.Taskbar;

/// <summary>
/// macOS shell adapter: macOS has no system-drawn Dock progress bar, so the
/// Dock tile's badge shows the percentage instead, and completion uses
/// <c>requestUserAttention:</c> with the informational (single-bounce, passive)
/// level. All calls go through <c>objc_msgSend</c> and run on the UI thread,
/// which is the AppKit main thread under Avalonia.
/// </summary>
[SupportedOSPlatform("macos")]
[ExcludeFromCodeCoverage]
internal sealed class MacDockTaskbarBackend : ITaskbarProgressBackend
{
    private const long NSInformationalRequest = 10;

    public void SetValue(double percent)
        => SetBadge($"{(int)Math.Clamp(percent, 0, 100)}%");

    public void SetIndeterminate() => SetBadge("…");

    public void Clear() => SetBadge(null);

    public void RequestAttention()
        => SendMessageLong(SharedApplication(), GetSelector("requestUserAttention:"), NSInformationalRequest);

    private static void SetBadge(string? text)
    {
        var dockTile = SendMessage(SharedApplication(), GetSelector("dockTile"));
        if (dockTile == IntPtr.Zero)
        {
            return;
        }

        var label = text is null
            ? IntPtr.Zero
            : SendMessageString(GetClass("NSString"), GetSelector("stringWithUTF8String:"), text);
        SendMessage(dockTile, GetSelector("setBadgeLabel:"), label);
        SendMessage(dockTile, GetSelector("display"));
    }

    private static IntPtr SharedApplication()
        => SendMessage(GetClass("NSApplication"), GetSelector("sharedApplication"));

    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr GetSelector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessageString(
        IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern long SendMessageLong(IntPtr receiver, IntPtr selector, long arg);
}
