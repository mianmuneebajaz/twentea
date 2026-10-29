using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using Twentea.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Twentea.Windows;

/// <summary>Wires the store, scheduler, stats and break windows to the tray icon and its menu.</summary>
sealed class TrayApp : INotifier
{
    private readonly WindowsActivity _activity = new();
    private readonly BreakPresenter _presenter = new();
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Drawing.Icon _onIcon;
    private readonly Drawing.Icon _offIcon;
    private readonly Dictionary<string, Drawing.Icon> _badges = [];

    public SettingsStore Store { get; } = new();
    public StatsLog Stats { get; } = new();
    public Scheduler Scheduler { get; }
    public Updater Updater { get; }

    /// <summary>Raised after power, preset or settings changes so open windows can refresh.</summary>
    public event Action? Changed;

    public TrayApp()
    {
        var size = Forms.SystemInformation.SmallIconSize;
        using (var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("twentea.ico")!)
            _onIcon = new Drawing.Icon(stream, size);
        _offIcon = Slashed(_onIcon);

        Scheduler = new Scheduler(Store.Settings, _activity, _presenter, this, Stats) { IsOn = Store.IsOn };
        _presenter.Scheduler = Scheduler;
        Store.Changed += () =>
        {
            Scheduler.Settings = Store.Settings;
            Changed?.Invoke();
        };

        Updater = new Updater(Quit);
        Updater.Changed += () => Changed?.Invoke();

        _menu.Opening += (_, _) => BuildMenu();
        _tray = new Forms.NotifyIcon { Visible = true, ContextMenuStrip = _menu };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) TrayPanel.Toggle(this);
        };

        _timer.Tick += (_, _) => { Scheduler.Tick(DateTime.Now); Refresh(); };
        _timer.Start();
        Updater.Start();
        Refresh();

        if (Store.IsFirstLaunch)
        {
            Store.MarkLaunched();
            SettingsWindow.Open(this);
        }
    }

    public void SetOn(bool on)
    {
        Store.IsOn = on;
        Scheduler.IsOn = on;
        Refresh();
        Changed?.Invoke();
    }

    public void ApplyPreset(string id)
    {
        Store.Apply(id);
        Refresh();
    }

    /// <summary>Same URLs and behaviour as the Mac app, see docs/TECHNICAL.md.</summary>
    public void Handle(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || url.Scheme != "twentea") return;
        var query = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
        switch (url.Host)
        {
            case "on": SetOn(true); break;
            case "off": SetOn(false); break;
            case "toggle": SetOn(!Store.IsOn); break;
            case "break": Scheduler.StartBreak(query.GetValueOrDefault("type") == "long" ? BreakKind.Long : BreakKind.Micro); break;
            case "pause": Scheduler.Pause(int.TryParse(query.GetValueOrDefault("minutes"), out var m) ? m : 30); break;
            case "resume": Scheduler.Resume(); break;
            case "preset": if (query.GetValueOrDefault("id") is { } id) ApplyPreset(id); break;
            case "settings": SettingsWindow.Open(this); break;
        }
        Refresh();
    }

    public void Quit()
    {
        _timer.Stop();
        _presenter.Hide();
        _tray.Visible = false;
        _tray.Dispose();
        _activity.Dispose();
        Application.Current.Shutdown();
    }

    public string StatsText()
    {
        var taken = Stats.Count(BreakOutcome.Done) + Stats.Count(BreakOutcome.Credited);
        var skipped = Stats.Count(BreakOutcome.Skipped);
        return $"Today: {taken} taken · {skipped} skipped" + (Stats.Adherence is { } a ? $" · {a}%" : "");
    }

    public string Status() => Scheduler.Phase switch
    {
        Phase.Off => "No breaks, dimming or reminders.",
        Phase.OffHours => "Outside working hours",
        Phase.Paused { Until: { } until } => $"Paused until {until:t}",
        Phase.Paused => "Paused",
        Phase.SmartPaused p => p.Reason,
        Phase.Due d => $"{d.Kind.Title()} at the next pause",
        Phase.OnBreak b => $"On {b.Session.Kind.Title().ToLowerInvariant()}",
        _ => string.Join(" · ", Enum.GetValues<BreakKind>()
            .Where(k => Scheduler.Remaining(k) is not null)
            .Select(k => $"{k.Title()}: {(int)Math.Ceiling(Scheduler.Remaining(k)!.Value / 60)} min")),
    };

    private void Refresh()
    {
        _tray.Icon = Store.IsOn ? Badge() ?? _onIcon : _offIcon;
        var text = "Twentea: " + Status();
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>
    /// Windows cannot show text beside a tray icon, so the countdown replaces the icon with a numbered badge.
    /// </summary>
    private Drawing.Icon? Badge()
    {
        if (!Store.Settings.Features.MenuBarCountdown) return null;
        string? text = Scheduler.Phase switch
        {
            Phase.Due => "!",
            Phase.OnBreak { Session: var s } => Short((s.EndsAt - DateTime.Now).TotalSeconds),
            Phase.Working when Scheduler.NextBreak is { } next => Short(next.Seconds),
            _ => null,
        };
        if (text is null) return null;
        if (_badges.TryGetValue(text, out var cached)) return cached;

        var size = Forms.SystemInformation.SmallIconSize;
        using var bitmap = new Drawing.Bitmap(size.Width, size.Height);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var rect = new Drawing.RectangleF(0, 0, size.Width - 1, size.Height - 1);
            using var path = Rounded(rect, size.Width * 0.28f);
            using var fill = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xE0, 0x7A, 0x1C));
            g.FillPath(fill, path);
            var fontSize = size.Height * (text.Length >= 3 ? 0.42f : text.Length == 2 ? 0.56f : 0.66f);
            using var font = new Drawing.Font("Segoe UI", fontSize, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            using var format = new Drawing.StringFormat { Alignment = Drawing.StringAlignment.Center, LineAlignment = Drawing.StringAlignment.Center };
            g.DrawString(text, font, Drawing.Brushes.White, new Drawing.RectangleF(0, 0, size.Width, size.Height + 1), format);
        }
        var icon = Drawing.Icon.FromHandle(bitmap.GetHicon());
        _badges[text] = icon;
        return icon;
    }

    /// <summary>Whole minutes from a minute up, seconds below that.</summary>
    private static string Short(double seconds)
    {
        var s = Math.Max(0, (int)Math.Ceiling(seconds));
        return (s >= 60 ? s / 60 : s).ToString();
    }

    private static GraphicsPath Rounded(Drawing.RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Right-click menu. Everything else lives in the tray panel that left click opens.</summary>
    private void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new Forms.ToolStripMenuItem("Open Twentea", null, (_, _) => TrayPanel.Toggle(this)) { Font = new Drawing.Font(_menu.Font, Drawing.FontStyle.Bold) });
        _menu.Items.Add(Store.IsOn ? "Turn off" : "Turn on", null, (_, _) => SetOn(!Store.IsOn));
        if (Updater.State == Updater.UpdateState.Available)
            _menu.Items.Add($"Update to {Updater.AvailableVersion}", null, (_, _) => _ = Updater.InstallAsync());
        _menu.Items.Add("Settings…", null, (_, _) => SettingsWindow.Open(this));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Quit", null, (_, _) => Quit());
    }

    /// <summary>The tray icon with a diagonal line through it, like the Mac menu bar icon when off.</summary>
    private static Drawing.Icon Slashed(Drawing.Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = bitmap.Width, h = bitmap.Height, inset = w * 0.08f;
            using var gap = new Drawing.Pen(Drawing.Color.White, w * 0.22f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var line = new Drawing.Pen(Drawing.Color.FromArgb(40, 40, 40), w * 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(gap, inset, inset, w - inset, h - inset);
            g.DrawLine(line, inset, inset, w - inset, h - inset);
        }
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    // INotifier

    public void NotifyBreak(BreakKind kind, int seconds)
    {
        var tip = kind == BreakKind.Micro
            ? $"Look at something at least 6 metres away for {seconds} seconds. Blink slowly."
            : $"Stand up and walk around for {Math.Max(1, seconds / 60)} minutes. Keep your eyes off screens.";
        _tray.ShowBalloonTip(5000, kind.Title(), tip, Forms.ToolTipIcon.None);
    }

    public void PlayStart() => SystemSounds.Asterisk.Play();

    public void PlayEnd() => SystemSounds.Exclamation.Play();
}
