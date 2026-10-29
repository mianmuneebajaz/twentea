using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Twentea.Core;

namespace Twentea.Windows;

/// <summary>Presets, schedule, breaks, features, smart pause and general settings. Mirrors mac/Sources/Twentea/UI/SettingsView.swift.</summary>
sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private readonly TrayApp _app;
    private readonly TabControl _tabs = new() { TabStripPlacement = Dock.Left, BorderThickness = new Thickness(0) };
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _power = new() { Padding = new Thickness(16, 6, 16, 6), FontWeight = FontWeights.SemiBold };
    private readonly (string Name, Func<FrameworkElement> Build)[] _pages;

    public static void Open(TrayApp app)
    {
        if (_open is null)
        {
            _open = new SettingsWindow(app);
            _open.Closed += (_, _) => _open = null;
            _open.Show();
        }
        if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    private SettingsWindow(TrayApp app)
    {
        _app = app;
        Title = "Twentea Settings";
        Width = 760;
        Height = 620;
        MinWidth = 620;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        using (var stream = typeof(SettingsWindow).Assembly.GetManifestResourceStream("twentea.ico"))
            if (stream is not null) Icon = System.Windows.Media.Imaging.BitmapFrame.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

        _pages =
        [
            ("Presets", PresetsPage),
            ("Schedule", SchedulePage),
            ("Breaks", BreaksPage),
            ("Features", FeaturesPage),
            ("Smart pause", SmartPausePage),
            ("General", GeneralPage),
        ];
        foreach (var (name, _) in _pages)
            _tabs.Items.Add(new TabItem { Header = name, Padding = new Thickness(14, 8, 14, 8), MinWidth = 130 });
        _tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == _tabs) ShowPage();
        };

        _power.Click += (_, _) => _app.SetOn(!_app.Store.IsOn);
        var header = new DockPanel { Margin = new Thickness(16, 14, 16, 10) };
        DockPanel.SetDock(_power, Dock.Right);
        header.Children.Add(_power);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _title, _status } });

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_tabs);
        Content = root;

        _app.Changed += RefreshHeader;
        Closed += (_, _) => _app.Changed -= RefreshHeader;
        _tabs.SelectedIndex = 0;
        RefreshHeader();
    }

    private SettingsStore Store => _app.Store;
    private AppSettings S => _app.Store.Settings;

    private void RefreshHeader()
    {
        _title.Text = Store.ActiveTitle;
        _status.Text = Store.IsOn ? "" : "Twentea is off";
        _power.Content = Store.IsOn ? "Turn off" : "Turn on";
    }

    private void ShowPage()
    {
        if (_tabs.SelectedItem is not TabItem tab) return;
        var page = _pages[_tabs.SelectedIndex].Build();
        tab.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(18, 4, 18, 18),
            Content = page,
        };
    }

    private void Set(Action<AppSettings> change)
    {
        Store.Update(change);
        RefreshHeader();
    }

    // Presets

    private FrameworkElement PresetsPage()
    {
        var page = Page();
        page.Children.Add(Note("Pick a preset, or change any setting and save it as your own. Your presets can be exported as .twentea files, which the Mac app also opens."));
        var list = new ListBox { Height = 240, Margin = new Thickness(0, 6, 0, 8) };
        foreach (var preset in Store.AllPresets)
        {
            list.Items.Add(new ListBoxItem
            {
                Content = preset.BuiltIn ? preset.Name : $"{preset.Name}   (mine)",
                Tag = preset.Id,
                FontWeight = preset.Id == Store.ActivePresetID ? FontWeights.SemiBold : FontWeights.Normal,
                Padding = new Thickness(6, 4, 6, 4),
            });
        }
        list.SelectedIndex = Store.AllPresets.ToList().FindIndex(p => p.Id == Store.ActivePresetID);
        page.Children.Add(list);

        Preset? Selected() => list.SelectedItem is ListBoxItem { Tag: string id } ? Store.AllPresets.FirstOrDefault(p => p.Id == id) : null;
        void Reload() { ShowPage(); RefreshHeader(); }

        var buttons = new WrapPanel();
        buttons.Children.Add(Act("Use", () => { if (Selected() is { } p) { _app.ApplyPreset(p.Id); Reload(); } }));
        buttons.Children.Add(Act("Save current as…", () =>
        {
            if (Prompt("Name for the new preset", "My preset") is { } name) { Store.SaveAsPreset(name); Reload(); }
        }));
        if (Store.IsCustom && Store.ActivePreset is { BuiltIn: false })
            buttons.Children.Add(Act("Update my preset", () => { Store.UpdateActivePreset(); Reload(); }));
        if (Store.IsCustom)
            buttons.Children.Add(Act("Discard changes", () => { Store.ResetToPreset(); Reload(); }));
        buttons.Children.Add(Act("Rename…", () =>
        {
            if (Selected() is { BuiltIn: false } p && Prompt("New name", p.Name) is { } name) { Store.RenamePreset(p.Id, name); Reload(); }
        }));
        buttons.Children.Add(Act("Duplicate", () => { if (Selected() is { } p) { Store.DuplicatePreset(p.Id); Reload(); } }));
        buttons.Children.Add(Act("Delete", () =>
        {
            if (Selected() is { BuiltIn: false } p &&
                MessageBox.Show(this, $"Delete \"{p.Name}\"?", "Twentea", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            { Store.DeletePreset(p.Id); Reload(); }
        }));
        buttons.Children.Add(Act("Import…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Twentea preset (*.twentea)|*.twentea|JSON (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { Store.ImportPreset(File.ReadAllText(dialog.FileName)); Reload(); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            { MessageBox.Show(this, "That file is not a Twentea preset.", "Twentea"); }
        }));
        buttons.Children.Add(Act("Export…", () =>
        {
            if (Selected() is not { } p) return;
            var dialog = new SaveFileDialog { Filter = "Twentea preset (*.twentea)|*.twentea", FileName = $"{p.Name}.twentea" };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Store.ExportPreset(p.Id));
        }));
        page.Children.Add(buttons);
        page.Children.Add(Note("Built-in presets can be duplicated but not renamed or deleted."));
        return page;
    }

    // Schedule

    private FrameworkElement SchedulePage()
    {
        var page = Page();
        page.Children.Add(Check("Only run during working hours", S.Schedule.Enabled, v => { Set(s => s.Schedule.Enabled = v); ShowPage(); }));
        page.Children.Add(Note("A window that ends at or before its start runs past midnight, for example 14:00 to 00:00."));
        if (!S.Schedule.Enabled) return page;

        page.Children.Add(Heading("Week"));
        foreach (var weekday in new[] { 2, 3, 4, 5, 6, 7, 1 })
        {
            var day = S.Schedule.Days.First(d => d.Weekday == weekday);
            if (day.Windows.Count == 0) day.Windows.Add(new TimeWindow { StartMinute = 9 * 60, EndMinute = 18 * 60 });
            var window = day.Windows[0];
            DaySchedule Day(AppSettings s) => s.Schedule.Days.First(d => d.Weekday == weekday);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)(weekday - 1));
            var enabled = new CheckBox { Content = name, IsChecked = day.Enabled, Width = 140, VerticalAlignment = VerticalAlignment.Center };
            enabled.Checked += (_, _) => Set(s => Day(s).Enabled = true);
            enabled.Unchecked += (_, _) => Set(s => Day(s).Enabled = false);
            row.Children.Add(enabled);
            row.Children.Add(TimeCombo(window.StartMinute, m => Set(s => Day(s).Windows[0].StartMinute = m)));
            row.Children.Add(new TextBlock { Text = "to", Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(TimeCombo(window.EndMinute, m => Set(s => Day(s).Windows[0].EndMinute = m)));
            if (day.Windows.Count > 1)
                row.Children.Add(new TextBlock { Text = $"+{day.Windows.Count - 1} more", Foreground = Brushes.Gray, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            page.Children.Add(row);
        }
        page.Children.Add(Act("Copy Monday to weekdays", () =>
        {
            Set(s =>
            {
                var monday = s.Schedule.Days.First(d => d.Weekday == 2);
                foreach (var d in s.Schedule.Days.Where(d => d.Weekday is >= 3 and <= 6))
                {
                    d.Enabled = monday.Enabled;
                    d.Windows = Json.Clone(monday.Windows);
                }
            });
            ShowPage();
        }));
        return page;
    }

    private static ComboBox TimeCombo(int minute, Action<int> set)
    {
        var options = Enumerable.Range(0, 48).Select(i => i * 30).ToList();
        if (!options.Contains(minute)) { options.Add(minute); options.Sort(); }
        var combo = new ComboBox { Width = 90, ItemsSource = options.Select(m => $"{m / 60:00}:{m % 60:00}").ToList(), SelectedIndex = options.IndexOf(minute) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set(options[combo.SelectedIndex]); };
        return combo;
    }

    // Breaks

    private FrameworkElement BreaksPage()
    {
        var page = Page();
        BreakEditor(page, BreakKind.Micro, [5, 10, 15, 20, 25, 30, 40, 45, 60], [10, 15, 20, 30, 40, 60, 90]);
        BreakEditor(page, BreakKind.Long, [30, 40, 45, 50, 60, 75, 90, 120], [60, 120, 180, 300, 600, 900]);
        page.Children.Add(Heading("Timing"));
        page.Children.Add(Choice("Wait for a natural pause up to", [0, 60, 120, 180, 300], S.BreakpointGraceSeconds,
            v => v == 0 ? "Don't wait" : $"{v / 60} min", v => Set(s => s.BreakpointGraceSeconds = v)));
        page.Children.Add(Note("When a break is due, Twentea waits until you stop typing or switch apps, up to this long."));
        return page;
    }

    private void BreakEditor(StackPanel page, BreakKind kind, int[] intervals, int[] durations)
    {
        var cfg = S.Config(kind);
        page.Children.Add(Heading(kind.Title()));
        page.Children.Add(Check("Enabled", cfg.Enabled, v => { Set(s => s.Config(kind).Enabled = v); ShowPage(); }));
        if (!cfg.Enabled) return;

        page.Children.Add(Choice("Every", intervals, cfg.IntervalMinutes, v => $"{v} min", v => Set(s => s.Config(kind).IntervalMinutes = v)));
        page.Children.Add(Choice("For", durations, cfg.DurationSeconds, Duration, v => Set(s => s.Config(kind).DurationSeconds = v)));

        var levels = Enum.GetValues<Enforcement>();
        var style = new ComboBox
        {
            Width = 220,
            ItemsSource = levels.Select(l => S.Features.Allows(l) ? l.Title() : $"{l.Title()} (switched off)").ToList(),
            SelectedIndex = Array.IndexOf(levels, cfg.Enforcement),
        };
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedIndex < 0) return;
            Set(s => s.Config(kind).Enforcement = levels[style.SelectedIndex]);
            ShowPage();
        };
        page.Children.Add(Row("Shows as", style));
        var resolved = S.Features.Resolve(cfg.Enforcement);
        if (resolved != cfg.Enforcement)
            page.Children.Add(Note($"Falls back to {resolved.Title()} because a feature is switched off.", Brushes.DarkOrange));

        page.Children.Add(Choice("Postpones per break", [0, 1, 2, 3, 5, 99], cfg.MaxPostpones,
            v => v >= 99 ? "Unlimited" : v.ToString(), v => Set(s => s.Config(kind).MaxPostpones = v)));
        page.Children.Add(Choice("Postpone by", [1, 2, 5, 10, 15, 30], cfg.PostponeMinutes, v => $"{v} min", v => Set(s => s.Config(kind).PostponeMinutes = v)));
        page.Children.Add(Choice("Skip button after", [0, 3, 5, 10, 30], cfg.SkipDelaySeconds,
            v => v == 0 ? "Immediately" : $"{v} s", v => Set(s => s.Config(kind).SkipDelaySeconds = v)));
        page.Children.Add(Act($"Preview {kind.Title().ToLowerInvariant()}", () => _app.Scheduler.StartBreak(kind)));
    }

    private static string Duration(int seconds) => seconds < 60
        ? $"{seconds} s"
        : seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds / 60} min {seconds % 60} s";

    // Features

    private FrameworkElement FeaturesPage()
    {
        var page = Page();
        var f = S.Features;
        page.Children.Add(Heading("How breaks reach you"));
        page.Children.Add(Check("Screen dimming", f.ScreenDimming, v => Set(s => s.Features.ScreenDimming = v), "Breaks use the floating card or a notification instead."));
        page.Children.Add(Check("Full-screen overlay", f.FullOverlay, v => Set(s => s.Features.FullOverlay = v), "Soft dim becomes the strongest level."));
        page.Children.Add(Check("Hard lock", f.HardLock, v => Set(s => s.Features.HardLock = v), "Overlays always keep a Skip button."));
        page.Children.Add(Check("Floating card", f.FloatingCard, v => Set(s => s.Features.FloatingCard = v), "Breaks fall back to notifications."));
        page.Children.Add(Check("Notifications", f.Notifications, v => Set(s => s.Features.Notifications = v), "Breaks show only on the tray icon."));
        page.Children.Add(Check("Sounds", f.Sounds, v => Set(s => s.Features.Sounds = v), "Visual cues only."));
        page.Children.Add(Heading("Tray icon"));
        page.Children.Add(Check("Countdown on the tray icon", f.MenuBarCountdown, v => Set(s => s.Features.MenuBarCountdown = v), "Icon only."));
        page.Children.Add(Heading("Timing"));
        page.Children.Add(Check("Count time away as a break", f.IdleCredit, v => Set(s => s.Features.IdleCredit = v), "Breaks follow the timer even after you step away."));
        page.Children.Add(Check("Smart pause", f.SmartPause, v => Set(s => s.Features.SmartPause = v), "Breaks fire during calls and videos too."));
        page.Children.Add(Check("Restart a dimmed break when you move the mouse or type", f.RestartOnInput,
            v => Set(s => s.Features.RestartOnInput = v), "The countdown keeps running whatever you do."));
        return page;
    }

    // Smart pause

    private FrameworkElement SmartPausePage()
    {
        var page = Page();
        page.Children.Add(Check("Smart pause", S.Features.SmartPause, v => { Set(s => s.Features.SmartPause = v); ShowPage(); }));
        page.Children.Add(Note("The timer stops while any rule below matches. No permissions needed."));
        if (!S.Features.SmartPause) return page;

        page.Children.Add(Heading("Rules"));
        page.Children.Add(Check("Camera or microphone in use", S.SmartPause.CameraOrMic, v => Set(s => s.SmartPause.CameraOrMic = v)));
        page.Children.Add(Check("Video playing or screen kept awake", S.SmartPause.VideoPlaying, v => Set(s => s.SmartPause.VideoPlaying = v)));

        page.Children.Add(Heading("Apps"));
        page.Children.Add(Note("The timer also stops while one of these apps is in front."));
        var list = new ListBox { Height = 140, Margin = new Thickness(0, 4, 0, 6) };
        foreach (var name in S.SmartPause.ExcludedBundleIDs) list.Items.Add(name);
        page.Children.Add(list);
        var buttons = new WrapPanel();
        buttons.Children.Add(Act("Add app…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dialog.ShowDialog(this) != true) return;
            var name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            if (!S.SmartPause.ExcludedBundleIDs.Contains(name, StringComparer.OrdinalIgnoreCase))
                Set(s => s.SmartPause.ExcludedBundleIDs.Add(name));
            ShowPage();
        }));
        buttons.Children.Add(Act("Remove", () =>
        {
            if (list.SelectedItem is string name) { Set(s => s.SmartPause.ExcludedBundleIDs.Remove(name)); ShowPage(); }
        }));
        page.Children.Add(buttons);
        return page;
    }

    // General

    private FrameworkElement GeneralPage()
    {
        var page = Page();
        page.Children.Add(Check("Open at login", SystemIntegration.LaunchAtLogin, v => SystemIntegration.LaunchAtLogin = v));
        page.Children.Add(Heading("Today"));
        page.Children.Add(new TextBlock { Text = _app.StatsText(), Margin = new Thickness(0, 2, 0, 6) });
        page.Children.Add(Heading("Data"));
        var data = new WrapPanel();
        data.Children.Add(Act("Reset break timers", () => _app.Scheduler.ResetTimers()));
        data.Children.Add(Act("Show data folder", () => SystemIntegration.OpenFolder(SettingsStore.DefaultDirectory)));
        page.Children.Add(data);
        page.Children.Add(Heading("Automation"));
        page.Children.Add(Note("Run these from a shortcut, a script or the Run dialog."));
        foreach (var (label, url) in new[] { ("Start break", "twentea://break"), ("Pause", "twentea://pause?minutes=30"), ("Switch preset", "twentea://preset?id=night-owl"), ("On, off, toggle", "twentea://toggle") })
            page.Children.Add(Row(label, new TextBox { Text = url, IsReadOnly = true, Width = 260, BorderThickness = new Thickness(0), Background = Brushes.Transparent }));
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "dev";
        page.Children.Add(Heading("About"));
        page.Children.Add(Row("Twentea", new TextBlock { Text = version }));
        return page;
    }

    // Building blocks

    private static StackPanel Page() => new() { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 6),
    };

    private static TextBlock Note(string text, Brush? color = null) => new()
    {
        Text = text, Foreground = color ?? Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6),
    };

    private static CheckBox Check(string label, bool value, Action<bool> set, string? off = null)
    {
        var box = new CheckBox { IsChecked = value, Margin = new Thickness(0, 5, 0, 5) };
        box.Content = off is null
            ? label
            : new StackPanel { Children = { new TextBlock { Text = label }, new TextBlock { Text = "Off: " + off, Foreground = Brushes.Gray, FontSize = 11.5 } } };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
    }

    private static FrameworkElement Choice(string label, int[] options, int current, Func<int, string> text, Action<int> set)
    {
        var values = options.Contains(current) ? options.ToList() : options.Append(current).Order().ToList();
        var combo = new ComboBox { Width = 160, ItemsSource = values.Select(text).ToList(), SelectedIndex = values.IndexOf(current) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set(values[combo.SelectedIndex]); };
        return Row(label, combo);
    }

    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static Button Act(string label, System.Action onClick)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 6, 8, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private string? Prompt(string title, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 8, 0, 12), MinWidth = 280 };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        var dialog = new Window
        {
            Title = "Twentea", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = title },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true && box.Text.Trim() is { Length: > 0 } name ? name : null;
    }
}
