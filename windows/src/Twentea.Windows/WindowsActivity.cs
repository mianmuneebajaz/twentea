using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Twentea.Core;

namespace Twentea.Windows;

/// <summary>Reads idle time, foreground switches and call or presentation signals. Needs no permission prompts.</summary>
sealed class WindowsActivity : IActivitySource, IDisposable
{
    private const uint EventSystemForeground = 3;
    private const uint WinEventOutOfContext = 0;
    private const int SystemExecutionState = 16;
    private const uint EsDisplayRequired = 0x2;

    private readonly WinEventProc _onForeground;
    private readonly IntPtr _hook;

    public DateTime LastAppSwitch { get; private set; } = DateTime.MinValue;

    public WindowsActivity()
    {
        // Keep the delegate in a field so the GC does not collect it while Windows holds the pointer.
        _onForeground = (_, _, _, _, _, _, _) => LastAppSwitch = DateTime.Now;
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _onForeground, 0, 0, WinEventOutOfContext);
    }

    public double IdleSeconds
    {
        get
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return 0;
            return unchecked((uint)Environment.TickCount - info.Time) / 1000.0;
        }
    }

    public string? SmartPauseReason(SmartPauseRules rules)
    {
        if (rules.ExcludedBundleIDs.Count > 0 && ForegroundProcessName() is { } name &&
            rules.ExcludedBundleIDs.Contains(name, StringComparer.OrdinalIgnoreCase))
            return $"Paused for {name}";
        if (rules.CameraOrMic && (InUse("webcam") || InUse("microphone")))
            return "Paused during a call";
        if (rules.VideoPlaying && (FullScreenOrPresenting() || DisplayKeptAwake()))
            return "Paused while video plays";
        return null;
    }

    /// <summary>Windows records per-app camera and mic use under ConsentStore. LastUsedTimeStop = 0 means in use now.</summary>
    private static bool InUse(string capability)
    {
        using var root = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}");
        return root is not null && AnyActive(root);
    }

    private static bool AnyActive(RegistryKey key)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub is null) continue;
            if (sub.GetValue("LastUsedTimeStop") is long stop && stop == 0 &&
                sub.GetValue("LastUsedTimeStart") is long start && start > 0)
                return true;
            if (name == "NonPackaged" && AnyActive(sub)) return true;
        }
        return false;
    }

    /// <summary>Video players and call apps ask Windows to keep the display on, like the Mac display-sleep assertion.</summary>
    private static bool DisplayKeptAwake() =>
        CallNtPowerInformation(SystemExecutionState, IntPtr.Zero, 0, out var state, sizeof(uint)) == 0 &&
        (state & EsDisplayRequired) != 0;

    /// <summary>Full-screen apps, games and slide shows.</summary>
    private static bool FullScreenOrPresenting()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is 2 or 3 or 4; // QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE
    }

    private static string? ForegroundProcessName()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid == 0) return null;
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch (ArgumentException) { return null; }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength, out uint output, uint outputLength);
}
