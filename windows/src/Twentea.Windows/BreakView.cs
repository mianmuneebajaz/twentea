using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Twentea.Core;
using Path = System.Windows.Shapes.Path;

namespace Twentea.Windows;

/// <summary>Progress ring, countdown, tip and buttons. Mirrors mac/Sources/Twentea/UI/BreakView.swift.</summary>
sealed class BreakView : Border
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xF0, 0x8A, 0x24));
    private static readonly Brush Track = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));

    private readonly Scheduler _scheduler;
    private readonly bool _big;
    private readonly double _ringSize;
    private readonly double _stroke;
    private readonly Path _arc = new() { Stroke = Accent, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock _countdown = new() { Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _postpone = MakeButton("");
    private readonly Button _skip = MakeButton("Skip");
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private BreakSession _session;

    public BreakView(Scheduler scheduler, BreakSession session, bool big)
    {
        _scheduler = scheduler;
        _session = session;
        _big = big;
        _ringSize = big ? 140 : 64;
        _stroke = big ? 8 : 5;
        _arc.StrokeThickness = _stroke;
        _countdown.FontSize = big ? 34 : 17;
        _countdown.FontWeight = FontWeights.SemiBold;

        var ring = new Grid { Width = _ringSize, Height = _ringSize };
        ring.Children.Add(new Ellipse { Stroke = Track, StrokeThickness = _stroke, Margin = new Thickness(_stroke / 2) });
        ring.Children.Add(_arc);
        ring.Children.Add(_countdown);

        _postpone.Click += (_, _) => _scheduler.Postpone();
        _skip.Click += (_, _) => _scheduler.Skip();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, big ? 8 : 10, 0, 0) };
        buttons.Children.Add(_postpone);
        if (session.SkipAvailableAt is null) buttons.Children.Add(HoldToEnd());
        else buttons.Children.Add(_skip);

        var kind = session.Kind;
        if (big)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };
            stack.Children.Add(ring);
            stack.Children.Add(Text(kind.Title(), 30, FontWeights.Bold, Brushes.White, top: 22));
            stack.Children.Add(Text(Tip(kind), 18, FontWeights.Normal, Muted, top: 12));
            if (session.Level >= Enforcement.SoftDim && scheduler.Settings.Features.RestartOnInput)
                stack.Children.Add(Text("Moving the mouse or typing restarts the timer.", 13, FontWeights.Normal, Muted, top: 12));
            stack.Children.Add(buttons);
            Child = stack;
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(ring);
            var words = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Width = 220, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Text(kind.Title(), 15, FontWeights.SemiBold, Brushes.White, top: 0, left: true));
            words.Children.Add(Text(Tip(kind), 12.5, FontWeights.Normal, Muted, top: 4, left: true));
            row.Children.Add(words);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(row);
            stack.Children.Add(buttons);
            Child = stack;
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x24, 0x22, 0x20));
            CornerRadius = new CornerRadius(18);
            Padding = new Thickness(16);
        }

        _clock.Tick += (_, _) => Render();
        Loaded += (_, _) => { Render(); _clock.Start(); };
        Unloaded += (_, _) => _clock.Stop();
    }

    public BreakSession Session
    {
        set { _session = value; Render(); }
    }

    private void Render()
    {
        var now = DateTime.Now;
        var left = Math.Max(0, (_session.EndsAt - now).TotalSeconds);
        var total = Math.Max(1, (_session.EndsAt - _session.StartedAt).TotalSeconds);
        _countdown.Text = Format(left);
        _arc.Data = Arc(left / total);

        var postpones = _session.CanPostpone && _scheduler.PostponesLeft > 0;
        _postpone.Visibility = postpones ? Visibility.Visible : Visibility.Collapsed;
        _postpone.Content = $"Postpone {_scheduler.Settings.Config(_session.Kind).PostponeMinutes} min";

        if (_session.SkipAvailableAt is { } at)
        {
            var wait = (int)Math.Ceiling((at - now).TotalSeconds);
            _skip.Content = wait > 0 ? $"Skip ({wait})" : "Skip";
            _skip.IsEnabled = wait <= 0;
        }
    }

    /// <summary>Clockwise arc from twelve o'clock covering the remaining fraction.</summary>
    private Geometry Arc(double fraction)
    {
        var r = (_ringSize - _stroke) / 2;
        var c = _ringSize / 2;
        if (fraction >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        if (fraction <= 0.001) return Geometry.Empty;
        var angle = 2 * Math.PI * fraction;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry([figure]);
    }

    /// <summary>Hard lock has no Skip. Holding this for three seconds ends the break and logs it as skipped.</summary>
    private FrameworkElement HoldToEnd()
    {
        var label = new Border
        {
            BorderBrush = Muted, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(5, 0, 5, 0), Background = Brushes.Transparent,
            Child = new TextBlock { Text = "Hold to end early", Foreground = Brushes.White, FontSize = 13 },
            Cursor = Cursors.Hand,
        };
        var hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        hold.Tick += (_, _) => { hold.Stop(); _scheduler.ForceEnd(); };
        label.MouseLeftButtonDown += (_, _) => { label.CaptureMouse(); hold.Start(); };
        label.MouseLeftButtonUp += (_, _) => { label.ReleaseMouseCapture(); hold.Stop(); };
        label.LostMouseCapture += (_, _) => hold.Stop();
        return label;
    }

    private static Button MakeButton(string text) => new() { Content = text, Style = Ui.Look("PillButton") };

    private static TextBlock Text(string text, double size, FontWeight weight, Brush color, double top, bool left = false) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = color, TextWrapping = TextWrapping.Wrap,
        TextAlignment = left ? TextAlignment.Left : TextAlignment.Center, Margin = new Thickness(0, top, 0, 0),
        HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
    };

    public static string Format(double seconds)
    {
        var s = (int)Math.Ceiling(seconds);
        return s >= 60 ? $"{s / 60}:{s % 60:00}" : s.ToString();
    }

    public static string Tip(BreakKind kind) => kind == BreakKind.Micro
        ? "Look at something at least 6 metres away, ideally out a window. Blink slowly."
        : "Stand up, walk around, refill your tea. Keep your eyes off screens.";
}
