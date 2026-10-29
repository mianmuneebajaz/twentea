using System.Text.Json.Serialization;

namespace Twentea.Core;

/// <summary>How strongly a break interrupts the user, weakest first.</summary>
public enum Enforcement { MenuBar, Notification, Card, SoftDim, Overlay, Lock }

public enum BreakKind { Micro, Long }

public static class EnforcementText
{
    public static string Title(this Enforcement level) => level switch
    {
        Enforcement.MenuBar => "Tray only",
        Enforcement.Notification => "Notification",
        Enforcement.Card => "Floating card",
        Enforcement.SoftDim => "Soft dim",
        Enforcement.Overlay => "Full overlay",
        _ => "Hard lock",
    };

    public static string Title(this BreakKind kind) => kind == BreakKind.Micro ? "Micro break" : "Long break";
}

public sealed class BreakConfig
{
    public bool Enabled { get; set; }
    public int IntervalMinutes { get; set; }
    public int DurationSeconds { get; set; }
    public Enforcement Enforcement { get; set; }
    public int MaxPostpones { get; set; }
    public int PostponeMinutes { get; set; }
    public int SkipDelaySeconds { get; set; }
}

/// <summary>A working window in minutes after midnight. EndMinute &lt;= StartMinute crosses midnight.</summary>
public sealed class TimeWindow
{
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }

    [JsonIgnore]
    public bool CrossesMidnight => EndMinute <= StartMinute;
}

public sealed class DaySchedule
{
    /// <summary>1 = Sunday ... 7 = Saturday, same numbering as the Mac app.</summary>
    public int Weekday { get; set; }
    public bool Enabled { get; set; }
    public List<TimeWindow> Windows { get; set; } = [];
}

public sealed class WeekSchedule
{
    public bool Enabled { get; set; }
    public List<DaySchedule> Days { get; set; } = [];

    public static WeekSchedule Uniform(IReadOnlySet<int> weekdays, TimeWindow window) => new()
    {
        Enabled = true,
        Days = Enumerable.Range(1, 7).Select(d => new DaySchedule
        {
            Weekday = d,
            Enabled = weekdays.Contains(d),
            Windows = [new TimeWindow { StartMinute = window.StartMinute, EndMinute = window.EndMinute }],
        }).ToList(),
    };

    public static WeekSchedule Always() => new()
    {
        Enabled = false,
        Days = Enumerable.Range(1, 7).Select(d => new DaySchedule
        {
            Weekday = d,
            Enabled = true,
            Windows = [new TimeWindow()],
        }).ToList(),
    };

    /// <summary>True when the local time falls inside an enabled window, including windows that started yesterday.</summary>
    public bool IsActive(DateTime at)
    {
        if (!Enabled) return true;
        var weekday = (int)at.DayOfWeek + 1;
        var minute = at.Hour * 60 + at.Minute;
        var yesterday = weekday == 1 ? 7 : weekday - 1;

        foreach (var day in Days.Where(d => d.Enabled))
        {
            foreach (var w in day.Windows)
            {
                if (day.Weekday == weekday)
                {
                    if (w.CrossesMidnight ? minute >= w.StartMinute : minute >= w.StartMinute && minute < w.EndMinute)
                        return true;
                }
                else if (day.Weekday == yesterday && w.CrossesMidnight && minute < w.EndMinute)
                {
                    return true;
                }
            }
        }
        return false;
    }
}

/// <summary>Master switches. Turning one off degrades breaks to the next weaker level.</summary>
public sealed class Features
{
    public bool Notifications { get; set; }
    public bool Sounds { get; set; }
    public bool FloatingCard { get; set; }
    public bool ScreenDimming { get; set; }
    public bool FullOverlay { get; set; }
    public bool HardLock { get; set; }
    public bool MenuBarCountdown { get; set; }
    public bool IdleCredit { get; set; }
    public bool SmartPause { get; set; }
    /// <summary>During a dim or overlay, input restarts the countdown. Missing in old files, so it defaults on.</summary>
    public bool RestartOnInput { get; set; } = true;

    public bool Allows(Enforcement level) => level switch
    {
        Enforcement.MenuBar => true,
        Enforcement.Notification => Notifications,
        Enforcement.Card => FloatingCard,
        Enforcement.SoftDim => ScreenDimming,
        Enforcement.Overlay => FullOverlay,
        _ => HardLock,
    };

    /// <summary>Strongest allowed level at or below the requested one.</summary>
    public Enforcement Resolve(Enforcement requested)
    {
        for (var level = requested; level > Enforcement.MenuBar; level--)
            if (Allows(level)) return level;
        return Enforcement.MenuBar;
    }
}

public sealed class SmartPauseRules
{
    public bool CameraOrMic { get; set; }
    public bool VideoPlaying { get; set; }
    /// <summary>Bundle IDs on the Mac, process names on Windows.</summary>
    public List<string> ExcludedBundleIDs { get; set; } = [];
}

public sealed class AppSettings
{
    public BreakConfig Micro { get; set; } = new();
    public BreakConfig Long { get; set; } = new();
    public WeekSchedule Schedule { get; set; } = new();
    public Features Features { get; set; } = new();
    public SmartPauseRules SmartPause { get; set; } = new();
    public int BreakpointGraceSeconds { get; set; }

    public BreakConfig Config(BreakKind kind) => kind == BreakKind.Micro ? Micro : Long;
}
