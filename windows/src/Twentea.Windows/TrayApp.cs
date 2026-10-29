using System.Drawing.Drawing2D;
using System.Media;
using Twentea.Core;

namespace Twentea.Windows;

/// <summary>
/// The tray icon and its menu. Phase W0 shows breaks as notifications; cards, dimming and overlays come in W1.
/// </summary>
sealed class TrayApp : ApplicationContext, IBreakPresenter, INotifier
{
    private readonly SettingsStore _store = new();
    private readonly WindowsActivity _activity = new();
    private readonly Scheduler _scheduler;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Icon _onIcon;
    private readonly Icon _offIcon;

    public TrayApp()
    {
        using (var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("twentea.ico")!)
            _onIcon = new Icon(stream, SystemInformation.SmallIconSize);
        _offIcon = Slashed(_onIcon);

        _scheduler = new Scheduler(Supported(_store.Settings), _activity, this, this) { IsOn = _store.IsOn };
        _tray = new NotifyIcon { Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu(_tray.ContextMenuStrip);
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) SetOn(!_store.IsOn);
        };

        _timer.Tick += (_, _) => { _scheduler.Tick(DateTime.Now); Refresh(); };
        _timer.Start();
        Refresh();
    }

    private void SetOn(bool on)
    {
        _store.IsOn = on;
        _scheduler.IsOn = on;
        Refresh();
    }

    private void Refresh()
    {
        _tray.Icon = _store.IsOn ? _onIcon : _offIcon;
        var text = "Twentea: " + Status();
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    private string Status() => _scheduler.Phase switch
    {
        Phase.Off => "off",
        Phase.OffHours => "outside working hours",
        Phase.Paused { Until: { } until } => $"paused until {until:t}",
        Phase.Paused => "paused",
        Phase.SmartPaused p => p.Reason,
        Phase.Due d => $"{d.Kind.Title().ToLowerInvariant()} at the next pause",
        Phase.OnBreak b => $"on a {b.Session.Kind.Title().ToLowerInvariant()}",
        _ => _scheduler.NextBreak is { } next ? $"{next.Kind.Title().ToLowerInvariant()} in {Minutes(next.Seconds)}" : "on",
    };

    private static string Minutes(double seconds) => seconds >= 60 ? $"{(int)Math.Ceiling(seconds / 60)} min" : $"{(int)seconds} s";

    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem(_store.IsOn ? "Twentea is on" : "Twentea is off") { Enabled = false });
        menu.Items.Add(_store.IsOn ? "Turn off" : "Turn on", null, (_, _) => SetOn(!_store.IsOn));
        menu.Items.Add(new ToolStripSeparator());

        var breakNow = new ToolStripMenuItem("Break now", null, (_, _) => _scheduler.StartBreak(BreakKind.Micro)) { Enabled = _store.IsOn };
        menu.Items.Add(breakNow);

        if (_scheduler.Phase is Phase.Paused)
        {
            menu.Items.Add(new ToolStripMenuItem("Resume", null, (_, _) => _scheduler.Resume()) { Enabled = _store.IsOn });
        }
        else
        {
            var pause = new ToolStripMenuItem("Pause") { Enabled = _store.IsOn };
            pause.DropDownItems.Add("30 minutes", null, (_, _) => _scheduler.Pause(30));
            pause.DropDownItems.Add("1 hour", null, (_, _) => _scheduler.Pause(60));
            pause.DropDownItems.Add("Until tomorrow", null, (_, _) => _scheduler.PauseUntilTomorrow());
            menu.Items.Add(pause);
        }

        var presets = new ToolStripMenuItem("Preset: " + _store.ActiveTitle) { Enabled = _store.IsOn };
        foreach (var preset in _store.AllPresets)
        {
            var id = preset.Id;
            presets.DropDownItems.Add(new ToolStripMenuItem(preset.Name, null, (_, _) =>
            {
                _store.Apply(id);
                _scheduler.Settings = Supported(_store.Settings);
                Refresh();
            }) { Checked = id == _store.ActivePresetID });
        }
        menu.Items.Add(presets);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
    }

    /// <summary>The tray icon with a diagonal line through it, like the Mac menu bar icon when off.</summary>
    private static Icon Slashed(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = bitmap.Width, h = bitmap.Height, inset = w * 0.08f;
            using var gap = new Pen(Color.White, w * 0.22f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var line = new Pen(Color.FromArgb(40, 40, 40), w * 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(gap, inset, inset, w - inset, h - inset);
            g.DrawLine(line, inset, inset, w - inset, h - inset);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>
    /// Phase W0 has no card, dim, overlay or lock windows yet, so breaks resolve to a notification at most.
    /// Remove a line here when its window ships.
    /// </summary>
    private static AppSettings Supported(AppSettings settings)
    {
        var s = Json.Clone(settings);
        s.Features.FloatingCard = false;
        s.Features.ScreenDimming = false;
        s.Features.FullOverlay = false;
        s.Features.HardLock = false;
        return s;
    }

    // IBreakPresenter. Notification breaks arrive through NotifyBreak; W0 has no windows to show.

    public void Show(BreakSession session) { }

    public void Update(BreakSession session) { }

    public void Hide() { }

    // INotifier

    public void NotifyBreak(BreakKind kind, int seconds) => Toast(kind, seconds);

    public void PlayStart() => SystemSounds.Asterisk.Play();

    public void PlayEnd() => SystemSounds.Exclamation.Play();

    private void Toast(BreakKind kind, int seconds)
    {
        var tip = kind == BreakKind.Micro
            ? $"Look at something at least 6 metres away for {seconds} seconds. Blink slowly."
            : $"Stand up and walk around for {seconds / 60} minutes. Keep your eyes off screens.";
        _tray.ShowBalloonTip(5000, kind.Title(), tip, ToolTipIcon.None);
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _activity.Dispose();
        base.ExitThreadCore();
    }
}
