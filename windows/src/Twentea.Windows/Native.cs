using System.Runtime.InteropServices;

namespace Twentea.Windows;

/// <summary>Win32 calls for break windows, focus and power.</summary>
static class Native
{
    public const int WsExTransparent = 0x20;
    public const int WsExToolWindow = 0x80;
    public const int WsExNoActivate = 0x08000000;
    public const uint SwpNoActivate = 0x10;
    public static readonly IntPtr HwndTopmost = new(-1);

    private const int GwlExStyle = -20;
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x1;

    public static void AddExStyle(IntPtr hwnd, int style) =>
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)((long)GetWindowLongPtr(hwnd, GwlExStyle) | style));

    /// <summary>Stops idle system sleep while a break runs. The display may still sleep.</summary>
    public static void KeepSystemAwake(bool on) =>
        SetThreadExecutionState(on ? EsContinuous | EsSystemRequired : EsContinuous);

    /// <summary>A background process may not take focus on its own, so borrow the foreground thread's input.</summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        var theirs = GetWindowThreadProcessId(foreground, out _);
        var ours = GetCurrentThreadId();
        if (theirs != 0 && theirs != ours)
        {
            AttachThreadInput(ours, theirs, true);
            SetForegroundWindow(hwnd);
            AttachThreadInput(ours, theirs, false);
        }
        else
        {
            SetForegroundWindow(hwnd);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
}
