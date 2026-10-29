namespace Twentea.Core;

/// <summary>What the scheduler needs to know about the user. An interface so tests can drive it.</summary>
public interface IActivitySource
{
    double IdleSeconds { get; }
    DateTime LastAppSwitch { get; }
    string? SmartPauseReason(SmartPauseRules rules);
}

/// <summary>Shows and hides break UI. The scheduler only decides when and how strongly.</summary>
public interface IBreakPresenter
{
    void Show(BreakSession session);
    void Update(BreakSession session);
    void Hide();
}

public interface INotifier
{
    void NotifyBreak(BreakKind kind, int seconds);
    void PlayStart();
    void PlayEnd();
}

public enum BreakOutcome { Done, Skipped, Postponed, Credited }

public interface IStatsSink
{
    void Record(BreakKind kind, BreakOutcome outcome, DateTime at);
}

/// <summary>SkipAvailableAt null means skipping is not allowed.</summary>
public sealed record BreakSession(
    BreakKind Kind, Enforcement Level, DateTime StartedAt, DateTime EndsAt, bool CanPostpone, DateTime? SkipAvailableAt);

public abstract record Phase
{
    public sealed record Off : Phase;
    public sealed record OffHours : Phase;
    public sealed record Paused(DateTime? Until) : Phase;
    public sealed record SmartPaused(string Reason) : Phase;
    public sealed record Working : Phase;
    public sealed record Due(BreakKind Kind, DateTime Since) : Phase;
    public sealed record OnBreak(BreakSession Session) : Phase;
}

/// <summary>The break state machine, a line-for-line port of mac/Sources/Twentea/Core/Scheduler.swift. Tick runs once a second.</summary>
public sealed class Scheduler
{
    /// <summary>Active time only counts while the user was active within this window.</summary>
    public const double ActiveIdleLimit = 120;
    /// <summary>A due break starts once input pauses this long.</summary>
    public const double BreakpointIdle = 3;

    private readonly IActivitySource _activity;
    private readonly IBreakPresenter? _presenter;
    private readonly INotifier? _notifier;
    private readonly IStatsSink? _stats;
    private readonly Dictionary<BreakKind, int> _postponesUsed = [];
    private DateTime? _lastTick;
    private bool _isOn = true;

    public Phase Phase { get; private set; } = new Phase.Working();
    public double MicroElapsed { get; private set; }
    public double LongElapsed { get; private set; }
    public DateTime Now { get; private set; } = DateTime.Now;
    public AppSettings Settings { get; set; }

    /// <summary>Raised after every tick and action so the UI can refresh.</summary>
    public event Action? Changed;

    public Scheduler(AppSettings settings, IActivitySource activity, IBreakPresenter? presenter,
                     INotifier? notifier = null, IStatsSink? stats = null)
    {
        Settings = settings;
        _activity = activity;
        _presenter = presenter;
        _notifier = notifier;
        _stats = stats;
    }

    /// <summary>Master switch. Off ends any break without logging it and stops all timers.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            if (value)
            {
                Phase = new Phase.Working();
            }
            else
            {
                _presenter?.Hide();
                Phase = new Phase.Off();
            }
            Changed?.Invoke();
        }
    }

    // Derived values

    public double? Remaining(BreakKind kind)
    {
        var cfg = Settings.Config(kind);
        if (!cfg.Enabled) return null;
        var elapsed = kind == BreakKind.Micro ? MicroElapsed : LongElapsed;
        return Math.Max(0, cfg.IntervalMinutes * 60 - elapsed);
    }

    /// <summary>The break that comes first, and how long until it.</summary>
    public (BreakKind Kind, double Seconds)? NextBreak
    {
        get
        {
            (BreakKind Kind, double Seconds)? best = null;
            foreach (var kind in Enum.GetValues<BreakKind>())
                if (Remaining(kind) is { } seconds && (best is null || seconds < best.Value.Seconds))
                    best = (kind, seconds);
            return best;
        }
    }

    public int PostponesLeft => Phase is Phase.OnBreak { Session: var s }
        ? Math.Max(0, Settings.Config(s.Kind).MaxPostpones - _postponesUsed.GetValueOrDefault(s.Kind))
        : 0;

    // Tick

    public void Tick(DateTime date)
    {
        Now = date;
        if (!IsOn)
        {
            Phase = new Phase.Off();
            _lastTick = date;
            Changed?.Invoke();
            return;
        }
        var dt = Math.Min(Math.Max((date - (_lastTick ?? date)).TotalSeconds, 0), 5);
        _lastTick = date;
        Step(date, dt);
        Changed?.Invoke();
    }

    private void Step(DateTime date, double dt)
    {
        if (Phase is Phase.OnBreak { Session: var session })
        {
            var cfg = Settings.Config(session.Kind);
            // During a dim or overlay, touching the keyboard or mouse restarts the countdown.
            // Moving StartedAt too keeps the progress ring in step with the new countdown.
            if (Settings.Features.RestartOnInput && session.Level >= Enforcement.SoftDim && _activity.IdleSeconds < 1 &&
                (date - session.StartedAt).TotalSeconds > 2)
            {
                session = session with { StartedAt = date, EndsAt = date.AddSeconds(cfg.DurationSeconds) };
                Phase = new Phase.OnBreak(session);
                _presenter?.Update(session);
            }
            if (date >= session.EndsAt) Finish(BreakOutcome.Done);
            return;
        }

        if (Phase is Phase.Paused paused)
        {
            if (paused.Until is { } until && date >= until) Phase = new Phase.Working();
            else return;
        }

        if (!Settings.Schedule.IsActive(date))
        {
            Phase = new Phase.OffHours();
            return;
        }

        if (Settings.Features.SmartPause && _activity.SmartPauseReason(Settings.SmartPause) is { } reason)
        {
            Phase = new Phase.SmartPaused(reason);
            return;
        }

        var idle = _activity.IdleSeconds;
        if (Settings.Features.IdleCredit) CreditIdle(idle, date);
        if (idle < ActiveIdleLimit)
        {
            MicroElapsed += dt;
            LongElapsed += dt;
        }

        if (Phase is Phase.Due due)
        {
            var grace = Settings.BreakpointGraceSeconds;
            if (idle >= BreakpointIdle || _activity.LastAppSwitch > due.Since || (date - due.Since).TotalSeconds >= grace)
                StartBreak(due.Kind, date);
            return;
        }

        Phase = DueKind() is { } kind ? new Phase.Due(kind, date) : new Phase.Working();
    }

    private BreakKind? DueKind()
    {
        if (Settings.Long.Enabled && LongElapsed >= Settings.Long.IntervalMinutes * 60) return BreakKind.Long;
        if (Settings.Micro.Enabled && MicroElapsed >= Settings.Micro.IntervalMinutes * 60) return BreakKind.Micro;
        return null;
    }

    /// <summary>Being away for a break's length counts as taking it.</summary>
    private void CreditIdle(double idle, DateTime date)
    {
        if (Settings.Long.Enabled && idle >= Settings.Long.DurationSeconds && LongElapsed > 60)
        {
            _stats?.Record(BreakKind.Long, BreakOutcome.Credited, date);
            LongElapsed = 0;
            MicroElapsed = 0;
        }
        else if (Settings.Micro.Enabled && idle >= Settings.Micro.DurationSeconds && MicroElapsed > 60)
        {
            _stats?.Record(BreakKind.Micro, BreakOutcome.Credited, date);
            MicroElapsed = 0;
        }
    }

    // Actions

    public void StartBreak(BreakKind kind, DateTime? at = null)
    {
        if (!IsOn) return;
        var date = at ?? DateTime.Now;
        var cfg = Settings.Config(kind);
        var level = Settings.Features.Resolve(cfg.Enforcement);
        var session = new BreakSession(
            kind, level, date, date.AddSeconds(cfg.DurationSeconds),
            CanPostpone: _postponesUsed.GetValueOrDefault(kind) < cfg.MaxPostpones && level != Enforcement.Lock,
            SkipAvailableAt: level == Enforcement.Lock ? (DateTime?)null : date.AddSeconds(cfg.SkipDelaySeconds));
        Phase = new Phase.OnBreak(session);
        _presenter?.Show(session);
        if (level == Enforcement.Notification) _notifier?.NotifyBreak(kind, cfg.DurationSeconds);
        if (Settings.Features.Sounds) _notifier?.PlayStart();
        Changed?.Invoke();
    }

    public void Postpone()
    {
        if (Phase is not Phase.OnBreak { Session: var s } || !s.CanPostpone || PostponesLeft <= 0) return;
        var cfg = Settings.Config(s.Kind);
        _postponesUsed[s.Kind] = _postponesUsed.GetValueOrDefault(s.Kind) + 1;
        double setBack = Math.Max(0, (cfg.IntervalMinutes - cfg.PostponeMinutes) * 60);
        if (s.Kind == BreakKind.Micro) MicroElapsed = setBack; else LongElapsed = setBack;
        _stats?.Record(s.Kind, BreakOutcome.Postponed, Now);
        _presenter?.Hide();
        Phase = new Phase.Working();
        Changed?.Invoke();
    }

    public void Skip()
    {
        if (Phase is not Phase.OnBreak { Session.SkipAvailableAt: { } at } || Now < at) return;
        Finish(BreakOutcome.Skipped);
    }

    /// <summary>Emergency exit, also used for hard lock. Logged as skipped.</summary>
    public void ForceEnd()
    {
        if (Phase is Phase.OnBreak) Finish(BreakOutcome.Skipped);
    }

    public void Pause(int? minutes)
    {
        if (Phase is Phase.OnBreak) Finish(BreakOutcome.Skipped);
        Phase = new Phase.Paused(minutes is { } m ? Now.AddMinutes(m) : (DateTime?)null);
        Changed?.Invoke();
    }

    public void PauseUntilTomorrow()
    {
        if (Phase is Phase.OnBreak) Finish(BreakOutcome.Skipped);
        Phase = new Phase.Paused(DayStart(Now).AddDays(1));
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (Phase is Phase.Paused) { Phase = new Phase.Working(); Changed?.Invoke(); }
    }

    public void ResetTimers()
    {
        MicroElapsed = 0;
        LongElapsed = 0;
    }

    /// <summary>Stats days start at 04:00 so a late session counts toward the day it began.</summary>
    public static DateTime DayStart(DateTime date) => date.AddHours(-4).Date.AddHours(4);

    private void Finish(BreakOutcome outcome)
    {
        if (Phase is not Phase.OnBreak { Session: var s }) return;
        _presenter?.Hide();
        if (s.Kind == BreakKind.Long) LongElapsed = 0;
        MicroElapsed = 0;
        _postponesUsed[s.Kind] = 0;
        _stats?.Record(s.Kind, outcome, Now);
        if (outcome == BreakOutcome.Done && Settings.Features.Sounds) _notifier?.PlayEnd();
        Phase = new Phase.Working();
    }
}
