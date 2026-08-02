using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace LincleLINK.App.Services.Taskbar;

/// <summary>
/// Windows shell adapter: drives the taskbar button's progress overlay through
/// <c>ITaskbarList3</c> and requests attention with <c>FlashWindowEx</c> (the
/// orange taskbar highlight that persists until the window is focused).
/// </summary>
[SupportedOSPlatform("windows")]
[ExcludeFromCodeCoverage]
internal sealed class WindowsTaskbarProgressBackend : ITaskbarProgressBackend
{
    private const uint FlashwTray = 0x00000002;
    private const uint FlashwTimerNoFg = 0x0000000C;

    private readonly Func<Window?> _windowProvider;
    private ITaskbarList3? _taskbarList;

    public WindowsTaskbarProgressBackend(Func<Window?> windowProvider)
    {
        _windowProvider = windowProvider;
    }

    public void SetValue(double percent)
    {
        var hwnd = GetHwnd();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var taskbar = GetTaskbarList();
        // SetProgressValue alone is documented to switch out of indeterminate,
        // but older shells only do so reliably after an explicit state change.
        taskbar.SetProgressState(hwnd, TaskbarProgressState.Normal);
        taskbar.SetProgressValue(hwnd, (ulong)Math.Clamp(percent, 0, 100), 100);
    }

    public void SetIndeterminate() => SetState(TaskbarProgressState.Indeterminate);

    public void Clear() => SetState(TaskbarProgressState.NoProgress);

    public void RequestAttention()
    {
        var hwnd = GetHwnd();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var info = new FlashWInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWInfo>(),
            Hwnd = hwnd,
            Flags = FlashwTray | FlashwTimerNoFg,
        };
        FlashWindowEx(ref info);
    }

    private void SetState(TaskbarProgressState state)
    {
        var hwnd = GetHwnd();
        if (hwnd != IntPtr.Zero)
        {
            GetTaskbarList().SetProgressState(hwnd, state);
        }
    }

    private IntPtr GetHwnd()
        => _windowProvider()?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    private ITaskbarList3 GetTaskbarList()
    {
        if (_taskbarList is null)
        {
            // CLSID_TaskbarList; the shell's canonical taskbar COM object.
            var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"))!;
            var taskbar = (ITaskbarList3)Activator.CreateInstance(type)!;
            taskbar.HrInit();
            _taskbarList = taskbar;
        }

        return _taskbarList;
    }

    private enum TaskbarProgressState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
    }

    /// <summary>
    /// Vtable-ordered prefix of ITaskbarList3 (through the two progress
    /// methods); trailing members this app never calls are omitted.
    /// </summary>
    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, int fullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TaskbarProgressState state);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWInfo info);
}
