using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Twentea.Core;
using Forms = System.Windows.Forms;

namespace Twentea.Windows;

/// <summary>
/// Draws breaks: floating card, soft dim, full overlay or lock, on every monitor.
/// Cards and dims never take focus, so typing into a terminal or agent session keeps working.
/// Overlay and lock take focus and hand it back to the previous window afterwards.
/// </summary>
sealed class BreakPresenter : IBreakPresenter
{
    private readonly List<Window> _windows = [];
    private readonly List<BreakView> _views = [];
    private IntPtr _previous;

    public Scheduler? Scheduler { get; set; }

    public void Show(BreakSession session)
    {
        Hide();
        if (Scheduler is not { } scheduler) return;
        Native.KeepSystemAwake(true);

        switch (session.Level)
        {
            case Enforcement.Card:
                AddCard(scheduler, session, centered: false);
                break;
            case Enforcement.SoftDim:
                AddDims(session, alpha: 0.45, blocking: false, scheduler: null);
                AddCard(scheduler, session, centered: true);
                break;
            case Enforcement.Overlay:
            case Enforcement.Lock:
                _previous = Native.GetForegroundWindow();
                AddDims(session, alpha: 0.9, blocking: true, scheduler: scheduler);
                break;
        }
    }

    public void Update(BreakSession session)
    {
        foreach (var view in _views) view.Session = session;
    }

    public void Hide()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        _views.Clear();
        Native.KeepSystemAwake(false);
        if (_previous != IntPtr.Zero)
        {
            Native.SetForegroundWindow(_previous);
            _previous = IntPtr.Zero;
        }
    }

    private void AddDims(BreakSession session, double alpha, bool blocking, Scheduler? scheduler)
    {
        var shade = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 0, 0, 0));
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var main = screen.Primary && scheduler is not null;
            var window = MakeWindow(clickThrough: !blocking, activate: main);
            window.Background = shade;
            if (main)
            {
                var view = new BreakView(scheduler!, session, big: true);
                _views.Add(view);
                window.Content = view;
            }
            var b = screen.Bounds;
            window.SourceInitialized += (_, _) =>
                Native.SetWindowPos(Handle(window), Native.HwndTopmost, b.X, b.Y, b.Width, b.Height, Native.SwpNoActivate);
            if (main) window.Loaded += (_, _) => { window.Activate(); Native.ForceForeground(Handle(window)); };
            FadeIn(window);
        }
    }

    /// <summary>Top right of the primary work area, or centered over a soft dim.</summary>
    private void AddCard(Scheduler scheduler, BreakSession session, bool centered)
    {
        const double width = 360, height = 180, margin = 16;
        var area = SystemParameters.WorkArea;
        var window = MakeWindow(clickThrough: false, activate: false);
        window.Width = width;
        window.Height = height;
        window.Left = centered ? area.Left + (area.Width - width) / 2 : area.Right - width - margin;
        window.Top = centered ? area.Top + (area.Height - height) / 2 : area.Top + margin;
        var view = new BreakView(scheduler, session, big: false)
        {
            Margin = new Thickness(10),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35 },
        };
        _views.Add(view);
        window.Content = view;
        FadeIn(window);
    }

    private static Window MakeWindow(bool clickThrough, bool activate)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = activate,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Title = "Twentea break",
        };
        window.SourceInitialized += (_, _) =>
        {
            var style = Native.WsExToolWindow;
            if (!activate) style |= Native.WsExNoActivate;
            if (clickThrough) style |= Native.WsExTransparent;
            Native.AddExStyle(Handle(window), style);
        };
        return window;
    }

    private void FadeIn(Window window)
    {
        window.Opacity = 0;
        window.Show();
        window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(600)));
        _windows.Add(window);
    }

    private static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;
}
