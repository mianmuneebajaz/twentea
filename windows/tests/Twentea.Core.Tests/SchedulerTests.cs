using Twentea.Core;

namespace Twentea.Core.Tests;

// Mirrors mac/Tests/TwenteaTests. Test names match the Swift ones (case aside); scripts/check-parity.sh checks this.

sealed class FakeActivity : IActivitySource
{
    public double IdleSeconds { get; set; }
    public DateTime LastAppSwitch { get; set; } = DateTime.MinValue;
    public string? PauseReason { get; set; }
    public string? SmartPauseReason(SmartPauseRules rules) => PauseReason;
}

sealed class FakePresenter : IBreakPresenter
{
    public List<BreakSession> Shown { get; } = [];
    public int Hidden { get; private set; }
    public void Show(BreakSession session) => Shown.Add(session);
    public void Update(BreakSession session) { }
    public void Hide() => Hidden++;
}

public class SchedulerTests
{
    static readonly DateTime Start = DateTime.UnixEpoch.AddSeconds(1_800_000_000);

    static AppSettings Settings(string id = "balanced")
    {
        var s = Json.Clone(Presets.BuiltIns.First(p => p.Id == id).Settings);
        s.Schedule = WeekSchedule.Always();
        s.Features.SmartPause = true;
        return s;
    }

    /// <summary>Ticks once a second for the given number of seconds.</summary>
    static DateTime Run(Scheduler sched, DateTime from, int seconds)
    {
        var t = from;
        for (var i = 0; i < seconds; i++) { t = t.AddSeconds(1); sched.Tick(t); }
        return t;
    }

    [Fact]
    public void MicroBreakStartsAfterIntervalAtBreakpoint()
    {
        var activity = new FakeActivity();
        var presenter = new FakePresenter();
        var sched = new Scheduler(Settings(), activity, presenter);
        sched.Tick(Start);
        var t = Run(sched, Start, 20 * 60);
        Assert.IsType<Phase.Due>(sched.Phase);
        Assert.Equal(BreakKind.Micro, ((Phase.Due)sched.Phase).Kind);

        activity.IdleSeconds = 4; // user paused typing
        t = Run(sched, t, 1);
        Assert.Equal(BreakKind.Micro, presenter.Shown[0].Kind);
        Assert.Equal(Enforcement.SoftDim, presenter.Shown[0].Level);

        activity.IdleSeconds = 30;
        Run(sched, t, 21);
        Assert.IsType<Phase.Working>(sched.Phase);
        Assert.True(sched.MicroElapsed < 5);
    }

    [Fact]
    public void InputRestartsCountdownAndProgress()
    {
        var activity = new FakeActivity { IdleSeconds = 30 };
        var sched = new Scheduler(Settings(), activity, new FakePresenter());
        sched.Tick(Start);
        sched.StartBreak(BreakKind.Micro, Start);
        var t = Run(sched, Start, 10);
        activity.IdleSeconds = 0; // mouse moved
        t = Run(sched, t, 1);
        var s = Assert.IsType<Phase.OnBreak>(sched.Phase).Session;
        Assert.Equal(t, s.StartedAt);
        Assert.Equal(20.0, (s.EndsAt - s.StartedAt).TotalSeconds);

        var noRestart = Settings();
        noRestart.Features.RestartOnInput = false;
        var sched2 = new Scheduler(noRestart, activity, new FakePresenter());
        sched2.Tick(Start);
        sched2.StartBreak(BreakKind.Micro, Start);
        Run(sched2, Start, 21);
        Assert.IsType<Phase.Working>(sched2.Phase);
    }

    [Fact]
    public void OldSettingsWithoutNewSwitchStillLoad()
    {
        const string json = """{"notifications":true,"sounds":true,"floatingCard":true,"screenDimming":true,"fullOverlay":true,"hardLock":false,"menuBarCountdown":true,"idleCredit":true,"smartPause":true}""";
        Assert.True(Json.Read<Features>(json).RestartOnInput);
    }

    [Fact]
    public void GraceForcesBreakWhileTyping()
    {
        var presenter = new FakePresenter();
        var sched = new Scheduler(Settings(), new FakeActivity(), presenter);
        sched.Tick(Start);
        Run(sched, Start, 20 * 60 + 181);
        Assert.Single(presenter.Shown);
    }

    [Fact]
    public void IdleCreditResetsMicroTimer()
    {
        var activity = new FakeActivity();
        var sched = new Scheduler(Settings(), activity, new FakePresenter());
        sched.Tick(Start);
        var t = Run(sched, Start, 300);
        activity.IdleSeconds = 130;
        Run(sched, t, 1);
        Assert.True(sched.MicroElapsed < 2);
        Assert.True(sched.LongElapsed > 290);
    }

    [Fact]
    public void ReadingPauseIsNotABreak()
    {
        var activity = new FakeActivity();
        var sched = new Scheduler(Settings(), activity, new FakePresenter());
        sched.Tick(Start);
        var t = Run(sched, Start, 300);
        activity.IdleSeconds = 25; // reading, longer than the 20 s micro break
        Run(sched, t, 1);
        Assert.True(sched.MicroElapsed > 290);
    }

    [Fact]
    public void SmartPauseStopsTimer()
    {
        var activity = new FakeActivity { PauseReason = "Paused during a call" };
        var sched = new Scheduler(Settings(), activity, new FakePresenter());
        sched.Tick(Start);
        Run(sched, Start, 600);
        Assert.Equal(0.0, sched.MicroElapsed);
        Assert.Equal(new Phase.SmartPaused("Paused during a call"), sched.Phase);
    }

    [Fact]
    public void PostponeSetsTimerBackAndRespectsLimit()
    {
        var sched = new Scheduler(Settings(), new FakeActivity(), new FakePresenter());
        sched.Tick(Start);
        sched.StartBreak(BreakKind.Micro, Start);
        sched.Postpone();
        Assert.Equal((20 - 5) * 60.0, sched.MicroElapsed);
        sched.StartBreak(BreakKind.Micro, Start);
        sched.Postpone();
        sched.StartBreak(BreakKind.Micro, Start);
        Assert.False(Assert.IsType<Phase.OnBreak>(sched.Phase).Session.CanPostpone);
    }

    [Fact]
    public void PowerOffStopsEverything()
    {
        var presenter = new FakePresenter();
        var sched = new Scheduler(Settings(), new FakeActivity(), presenter);
        sched.Tick(Start);
        sched.StartBreak(BreakKind.Micro, Start);
        sched.IsOn = false;
        Assert.IsType<Phase.Off>(sched.Phase);
        Assert.Equal(1, presenter.Hidden);
        Run(sched, Start, 30 * 60);
        Assert.Equal(0.0, sched.MicroElapsed);
        sched.StartBreak(BreakKind.Micro, Start);
        Assert.IsType<Phase.Off>(sched.Phase);
        sched.IsOn = true;
        Assert.IsType<Phase.Working>(sched.Phase);
    }

    [Fact]
    public void SwitchedOffFeaturesDegradeEnforcement()
    {
        var f = Settings().Features;
        f.FullOverlay = false;
        f.ScreenDimming = false;
        Assert.Equal(Enforcement.Card, f.Resolve(Enforcement.Overlay));
        f.FloatingCard = false;
        Assert.Equal(Enforcement.Notification, f.Resolve(Enforcement.Lock));
        f.Notifications = false;
        Assert.Equal(Enforcement.MenuBar, f.Resolve(Enforcement.Lock));
    }

    [Fact]
    public void ScheduleCrossesMidnight()
    {
        var schedule = WeekSchedule.Uniform(new HashSet<int> { 2 }, new TimeWindow { StartMinute = 14 * 60, EndMinute = 0 });
        // 2026-11-02 is a Monday.
        var mon1500 = new DateTime(2026, 11, 2, 15, 0, 0);
        var mon1300 = new DateTime(2026, 11, 2, 13, 0, 0);
        var tue0030 = new DateTime(2026, 11, 3, 0, 30, 0);
        Assert.True(schedule.IsActive(mon1500));
        Assert.False(schedule.IsActive(mon1300));
        Assert.False(schedule.IsActive(tue0030));

        var late = WeekSchedule.Uniform(new HashSet<int> { 2 }, new TimeWindow { StartMinute = 14 * 60, EndMinute = 2 * 60 });
        Assert.True(late.IsActive(tue0030));
    }

    [Fact]
    public void StoreDetectsCustomAndSavesPresets()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(dir);
        Assert.True(store.IsFirstLaunch);
        Assert.False(store.IsCustom);
        store.Update(s => s.Micro.IntervalMinutes = 25);
        Assert.True(store.IsCustom);
        var saved = store.SaveAsPreset("Mine");
        Assert.False(store.IsCustom);
        Assert.Equal(saved.Id, store.ActivePresetID);

        var reopened = new SettingsStore(dir);
        Assert.Equal(new[] { "Mine" }, reopened.UserPresets.Select(p => p.Name));
        Assert.Equal(25, reopened.Settings.Micro.IntervalMinutes);

        var data = reopened.ExportPreset(saved.Id);
        reopened.ImportPreset(data);
        Assert.Equal(2, reopened.UserPresets.Count);
    }
}

public class SharedPresetsTests
{
    [Fact]
    public void BuiltInPresetsMatchSharedFile()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "shared", "presets.json");
        var shared = Json.Read<List<Preset>>(File.ReadAllText(file));
        Assert.Equal(Json.Write(shared), Json.Write(Presets.BuiltIns));
        Assert.Equal(new[] { "gentle", "balanced", "strict", "night-owl", "meeting-heavy", "minimal" }, Presets.BuiltIns.Select(p => p.Id));
    }
}
