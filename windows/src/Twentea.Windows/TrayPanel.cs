using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Twentea.Core;

namespace Twentea.Windows;

/// <summary>
/// The panel that opens from the tray icon, like the Mac menu bar panel: power button, status,
/// break and pause actions, preset picker, today's stats and the update button.
/// </summary>
sealed class TrayPanel : Window
{
    private static TrayPanel? _open;
    private static DateTime _closedAt;

    private readonly TrayApp _app;
    private readonly PowerButton _power;
    private readonly TextBlock _headline = Ui.Text("", 16, FontWeights.SemiBold);
    private readonly TextBlock _status = Ui.Secondary("", 13);
    private readonly StackPanel _controls = new();
    private readonly StackPanel _update = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _closing;

    public static void Toggle(TrayApp app)
    {
        if (_open is not null) { _open.SafeClose(); return; }
        // Clicking the tray icon deactivates an open panel first; do not reopen it straight away.
        if (DateTime.Now - _closedAt < TimeSpan.FromMilliseconds(400)) return;
        _open = new TrayPanel(app);
        _open.Closed += (_, _) => { _open = null; _closedAt = DateTime.Now; };
        _open.Show();
    }

    private TrayPanel(TrayApp app)
    {
        _app = app;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        Title = "Twentea";

        _power = new PowerButton(88, () => _app.SetOn(!_app.Store.IsOn)) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 12) };
        _headline.HorizontalAlignment = HorizontalAlignment.Center;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.TextAlignment = TextAlignment.Center;
        _status.MinHeight = 34;

        var footer = new DockPanel();
        var quit = Ui.Button("Quit", () => _app.Quit(), "SubtleButton");
        quit.Margin = new Thickness(0);
        DockPanel.SetDock(quit, Dock.Right);
        footer.Children.Add(quit);
        footer.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Ui.Button("Settings", OpenSettings, "SubtleButton").With(b => b.Margin = new Thickness(0)) },
        });

        Content = Ui.Surface(new StackPanel
        {
            Children = { _update, _power, _headline, _status, _controls, Ui.Divider(), footer },
        });

        _app.Changed += Render;
        _clock.Tick += (_, _) => RenderStatus();
        Loaded += (_, _) =>
        {
            Place();
            Native.ForceForeground(new WindowInteropHelper(this).Handle);
            Activate();
            _clock.Start();
        };
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => SafeClose();
        Closed += (_, _) => { _clock.Stop(); _app.Changed -= Render; };
        Render();
    }

    /// <summary>Above the taskbar at the bottom right, or below it when the taskbar is at the top.</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        var cursor = System.Windows.Forms.Cursor.Position;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var cursorY = cursor.Y / dpi.DpiScaleY;
        Left = area.Right - ActualWidth + 6;
        Top = cursorY < area.Top + area.Height / 2 ? area.Top - 6 : area.Bottom - ActualHeight + 6;
    }

    private void Render()
    {
        var on = _app.Store.IsOn;
        _power.Refresh(on);
        _headline.Text = on ? "Twentea is on" : "Twentea is off";
        RenderStatus();

        _update.Children.Clear();
        if (_app.Updater.State == Updater.UpdateState.Available)
        {
            var button = Ui.Button($"Update to {_app.Updater.AvailableVersion}", () => _ = _app.Updater.InstallAsync(), "AccentButton");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 14);
            _update.Children.Add(button);
        }

        _controls.Children.Clear();
        _controls.IsEnabled = on;
        _controls.Opacity = on ? 1 : 0.4;
        _controls.Children.Add(Ui.Divider());

        var actions = new UniformGrid { Columns = 2 };
        actions.Children.Add(Ui.Button("Break now", () => _app.Scheduler.StartBreak(BreakKind.Micro)).With(b => b.Margin = new Thickness(0, 0, 4, 0)));
        if (_app.Scheduler.Phase is Phase.Paused)
        {
            actions.Children.Add(Ui.Button("Resume", () => { _app.Scheduler.Resume(); Render(); }).With(b => b.Margin = new Thickness(4, 0, 0, 0)));
        }
        else
        {
            var pause = Ui.Button("Pause", () => { }).With(b => b.Margin = new Thickness(4, 0, 0, 0));
            var menu = new ContextMenu();
            foreach (var (label, act) in new (string, Action)[]
            {
                ("30 minutes", () => _app.Scheduler.Pause(30)),
                ("1 hour", () => _app.Scheduler.Pause(60)),
                ("Until tomorrow", () => _app.Scheduler.PauseUntilTomorrow()),
            })
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => { act(); Render(); };
                menu.Items.Add(item);
            }
            pause.Click += (_, _) => { menu.PlacementTarget = pause; menu.IsOpen = true; };
            actions.Children.Add(pause);
        }
        _controls.Children.Add(actions);

        var presets = _app.Store.AllPresets.ToList();
        var picker = new ComboBox
        {
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = presets.Select(p => p.Name).ToList(),
            SelectedIndex = presets.FindIndex(p => p.Id == _app.Store.ActivePresetID),
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0 && presets[picker.SelectedIndex].Id != _app.Store.ActivePresetID)
                _app.ApplyPreset(presets[picker.SelectedIndex].Id);
        };
        _controls.Children.Add(Ui.Secondary(_app.Store.IsCustom ? "Preset (changed)" : "Preset").With(t => t.Margin = new Thickness(0, 12, 0, -6)));
        _controls.Children.Add(picker);
        _controls.Children.Add(Ui.Secondary(_app.Stats.Enabled ? _app.StatsText() : "Stats are off").With(t =>
        {
            t.Margin = new Thickness(0, 10, 0, 0);
            t.HorizontalAlignment = HorizontalAlignment.Center;
        }));
    }

    private void RenderStatus() => _status.Text = _app.Status();

    /// <summary>
    /// Closing a window deactivates it, and deactivation closes the panel. Calling Close on a window
    /// that is already closing throws, so every close goes through here.
    /// </summary>
    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    /// <summary>Closes the panel first, then opens settings once the close has finished.</summary>
    private void OpenSettings()
    {
        SafeClose();
        Dispatcher.InvokeAsync(() => SettingsWindow.Open(_app));
    }

    /// <summary>Used by the self-test: the same path as clicking Settings in an open panel.</summary>
    public static void OpenSettingsFromPanel() => _open?.OpenSettings();
}
