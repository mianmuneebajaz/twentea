using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Twentea.Core;

namespace Twentea.Windows;

/// <summary>
/// Settings in the Windows 11 Settings layout: sidebar on the left, one card per setting on the right.
/// Same pages and options as mac/Sources/Twentea/UI/SettingsView.swift.
/// </summary>
sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private readonly TrayApp _app;
    private readonly ListBox _nav = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(28, 24, 28, 28) };
    private readonly PowerButton _power;
    private readonly TextBlock _powerTitle = Ui.Text("", 14, FontWeights.SemiBold);
    private readonly TextBlock _powerSubtitle = Ui.Secondary("");
    private readonly (string Glyph, string Name, Func<StackPanel> Build)[] _pages;

    /// <summary>The open settings window, if any. Used by the self-test.</summary>
    public static SettingsWindow? Current => _open;

    public void ShowPageAt(int index) => _nav.SelectedIndex = index;

    public int PageCount => _pages.Length;

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
        Width = 920;
        Height = 680;
        MinWidth = 720;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        using (var stream = typeof(SettingsWindow).Assembly.GetManifestResourceStream("twentea.ico"))
            if (stream is not null) Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        _pages =
        [
            ("", "Presets", PresetsPage),
            ("", "Schedule", SchedulePage),
            ("", "Breaks", BreaksPage),
            ("", "Features", FeaturesPage),
            ("", "Smart pause", SmartPausePage),
            ("", "General", GeneralPage),
        ];

        _power = new PowerButton(40, () => _app.SetOn(!_app.Store.IsOn));
        Content = Layout();

        _nav.SelectionChanged += (_, _) => ShowPage();
        _app.Changed += Refresh;
        Closed += (_, _) => _app.Changed -= Refresh;
        _nav.SelectedIndex = 0;
        Refresh();
    }

    private FrameworkElement Layout()
    {
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 0, 18) };
        if (Icon is not null) brand.Children.Add(new Image { Source = Icon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(Ui.Text("Twentea", 18, FontWeights.SemiBold).With(t => t.VerticalAlignment = VerticalAlignment.Center));

        var powerCard = new Border { Style = Ui.Look("Card"), Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(12) };
        var powerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Children = { _powerTitle, _powerSubtitle } };
        powerCard.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _power, powerText } };

        _nav.ItemContainerStyle = Ui.Look("NavItem");
        foreach (var (glyph, name, _) in _pages)
        {
            _nav.Items.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { Ui.Glyph(glyph, 16), Ui.Text(name).With(t => { t.Margin = new Thickness(14, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center; }) },
            });
        }

        var sidebar = new DockPanel { Width = 280, Margin = new Thickness(12, 16, 4, 16) };
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(powerCard, Dock.Top);
        sidebar.Children.Add(brand);
        sidebar.Children.Add(powerCard);
        sidebar.Children.Add(_nav);

        var root = new DockPanel();
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(sidebar);
        root.Children.Add(_scroll);
        return root;
    }

    private SettingsStore Store => _app.Store;
    private AppSettings S => _app.Store.Settings;

    private void Refresh()
    {
        _power.Refresh(Store.IsOn);
        _powerTitle.Text = Store.IsOn ? "Twentea is on" : "Twentea is off";
        _powerSubtitle.Text = Store.ActiveTitle;
    }

    private void ShowPage()
    {
        if (_nav.SelectedIndex < 0) return;
        var (_, name, build) = _pages[_nav.SelectedIndex];
        var page = build();
        page.Children.Insert(0, Ui.Text(name, 28, FontWeights.SemiBold).With(t => t.Margin = new Thickness(0, 0, 0, 18)));
        _scroll.Content = page;
    }

    private void Set(Action<AppSettings> change)
    {
        Store.Update(change);
        Refresh();
    }

    // Presets

    private StackPanel PresetsPage()
    {
        var page = Page();
        if (Store.IsCustom && Store.ActivePreset is { } active)
        {
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(Ui.Button("Save as new preset…", SaveCurrent, "AccentButton"));
            if (!active.BuiltIn) actions.Children.Add(Ui.Button($"Update {active.Name}", () => { Store.UpdateActivePreset(); ShowPage(); }));
            actions.Children.Add(Ui.Button("Discard changes", () => { Store.ResetToPreset(); ShowPage(); }));
            page.Children.Add(Card(new StackPanel
            {
                Children = { Ui.Text($"You changed settings from {active.Name}", 14, FontWeights.SemiBold), Ui.Secondary("Keep them as a preset of your own, or go back."), actions },
            }));
        }

        page.Children.Add(Group("Built in"));
        foreach (var preset in Presets.BuiltIns) page.Children.Add(PresetCard(preset));
        page.Children.Add(Group("Yours"));
        foreach (var preset in Store.UserPresets) page.Children.Add(PresetCard(preset));
        if (Store.UserPresets.Count == 0)
            page.Children.Add(Card(Ui.Secondary("Change any setting, then save it here as your own preset.", 13)));

        var bottom = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        bottom.Children.Add(Ui.Button("Save current settings…", SaveCurrent));
        bottom.Children.Add(Ui.Button("Import…", Import));
        page.Children.Add(bottom);
        page.Children.Add(Note("Exported .twentea files open in the Mac app too."));
        return page;
    }

    private Border PresetCard(Preset preset)
    {
        var isActive = preset.Id == Store.ActivePresetID;
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (isActive)
            right.Children.Add(Ui.Text(Store.IsCustom ? "In use, changed" : "In use", 13, FontWeights.SemiBold, "AccentTextFillColorPrimaryBrush")
                .With(t => { t.Margin = new Thickness(0, 0, 12, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
        else
            right.Children.Add(Ui.Button("Use", () => { _app.ApplyPreset(preset.Id); ShowPage(); }));

        var more = Ui.Button("", () => { }, "SubtleButton");
        more.SetResourceReference(FontFamilyProperty, "IconFont");
        more.Margin = new Thickness(0);
        var menu = new ContextMenu();
        void Item(string label, Action act)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => act();
            menu.Items.Add(item);
        }
        if (!preset.BuiltIn) Item("Rename…", () => { if (Prompt("New name", preset.Name) is { } n) { Store.RenamePreset(preset.Id, n); ShowPage(); Refresh(); } });
        Item("Duplicate", () => { Store.DuplicatePreset(preset.Id); ShowPage(); });
        Item("Export…", () => Export(preset));
        if (!preset.BuiltIn) Item("Delete", () =>
        {
            if (MessageBox.Show(this, $"Delete \"{preset.Name}\"?", "Twentea", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            { Store.DeletePreset(preset.Id); ShowPage(); Refresh(); }
        });
        more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
        right.Children.Add(more);

        var s = preset.Settings;
        var summary = $"Every {s.Micro.IntervalMinutes} min, {s.Micro.Enforcement.Title().ToLowerInvariant()}" +
                      (s.Long.Enabled ? $". Long break every {s.Long.IntervalMinutes} min" : "");
        return Row(preset.Name, summary, right);
    }

    private void SaveCurrent()
    {
        if (Prompt("Name for the new preset", "My preset") is not { } name) return;
        Store.SaveAsPreset(name);
        ShowPage();
        Refresh();
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Twentea preset (*.twentea)|*.twentea|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { Store.ImportPreset(File.ReadAllText(dialog.FileName)); ShowPage(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        { MessageBox.Show(this, "That file is not a Twentea preset.", "Twentea"); }
    }

    private void Export(Preset preset)
    {
        var dialog = new SaveFileDialog { Filter = "Twentea preset (*.twentea)|*.twentea", FileName = $"{preset.Name}.twentea" };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Store.ExportPreset(preset.Id));
    }

    // Schedule

    private StackPanel SchedulePage()
    {
        var page = Page();
        page.Children.Add(Toggle("Only run during working hours", "Outside these hours Twentea stays quiet.",
            S.Schedule.Enabled, v => { Set(s => s.Schedule.Enabled = v); ShowPage(); }));
        if (!S.Schedule.Enabled) return page;

        page.Children.Add(Group("Week"));
        foreach (var weekday in new[] { 2, 3, 4, 5, 6, 7, 1 })
        {
            var day = S.Schedule.Days.First(d => d.Weekday == weekday);
            if (day.Windows.Count == 0) day.Windows.Add(new TimeWindow { StartMinute = 9 * 60, EndMinute = 18 * 60 });
            var window = day.Windows[0];
            DaySchedule Day(AppSettings s) => s.Schedule.Days.First(d => d.Weekday == weekday);

            var times = new StackPanel { Orientation = Orientation.Horizontal, IsEnabled = day.Enabled };
            times.Children.Add(TimeCombo(window.StartMinute, m => Set(s => Day(s).Windows[0].StartMinute = m)));
            times.Children.Add(Ui.Secondary("to", 13).With(t => { t.Margin = new Thickness(10, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center; }));
            times.Children.Add(TimeCombo(window.EndMinute, m => Set(s => Day(s).Windows[0].EndMinute = m)));
            var toggle = Switch(day.Enabled, v => { Set(s => Day(s).Enabled = v); times.IsEnabled = v; });
            toggle.Margin = new Thickness(24, 0, 0, 0);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { times, toggle } };

            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName((DayOfWeek)(weekday - 1));
            var note = window.CrossesMidnight ? "Runs past midnight" : day.Windows.Count > 1 ? $"{day.Windows.Count - 1} more window set on the Mac" : "";
            page.Children.Add(Row(name, note, right));
        }
        page.Children.Add(Ui.Button("Copy Monday to weekdays", () =>
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
        }).With(b => { b.Margin = new Thickness(0, 10, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Left; }));
        page.Children.Add(Note("A window that ends at or before its start runs past midnight, for example 14:00 to 00:00."));
        return page;
    }

    private static ComboBox TimeCombo(int minute, Action<int> set)
    {
        var options = Enumerable.Range(0, 48).Select(i => i * 30).ToList();
        if (!options.Contains(minute)) { options.Add(minute); options.Sort(); }
        var combo = new ComboBox { Width = 96, ItemsSource = options.Select(m => $"{m / 60:00}:{m % 60:00}").ToList(), SelectedIndex = options.IndexOf(minute) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set(options[combo.SelectedIndex]); };
        return combo;
    }

    // Breaks

    private StackPanel BreaksPage()
    {
        var page = Page();
        BreakEditor(page, BreakKind.Micro, "Look 6 metres away for a few seconds.", [5, 10, 15, 20, 25, 30, 40, 45, 60], [10, 15, 20, 30, 40, 60, 90]);
        BreakEditor(page, BreakKind.Long, "Stand up and step away from the screen.", [30, 40, 45, 50, 60, 75, 90, 120], [60, 120, 180, 300, 600, 900]);
        page.Children.Add(Group("Timing"));
        page.Children.Add(Choice("Wait for a natural pause", "When a break is due, Twentea waits until you stop typing or switch apps, up to this long.",
            [0, 60, 120, 180, 300], S.BreakpointGraceSeconds, v => v == 0 ? "Don't wait" : $"Up to {v / 60} min", v => Set(s => s.BreakpointGraceSeconds = v)));
        return page;
    }

    private void BreakEditor(StackPanel page, BreakKind kind, string description, int[] intervals, int[] durations)
    {
        var cfg = S.Config(kind);
        page.Children.Add(Group(kind.Title()));
        page.Children.Add(Toggle(kind.Title(), description, cfg.Enabled, v => { Set(s => s.Config(kind).Enabled = v); ShowPage(); }));
        if (!cfg.Enabled) return;

        page.Children.Add(Choice("Every", "Minutes of screen time between breaks.", intervals, cfg.IntervalMinutes, v => $"{v} min", v => Set(s => s.Config(kind).IntervalMinutes = v)));
        page.Children.Add(Choice("Length", "How long the break lasts.", durations, cfg.DurationSeconds, Duration, v => Set(s => s.Config(kind).DurationSeconds = v)));

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
        var resolved = S.Features.Resolve(cfg.Enforcement);
        page.Children.Add(Row("Shows as", resolved != cfg.Enforcement
            ? $"Falls back to {resolved.Title()} because a feature is switched off."
            : "How strongly the break interrupts you.", style));

        page.Children.Add(Choice("Postpones per break", "How many times you can push a break back.", [0, 1, 2, 3, 5, 99], cfg.MaxPostpones,
            v => v >= 99 ? "Unlimited" : v.ToString(), v => Set(s => s.Config(kind).MaxPostpones = v)));
        page.Children.Add(Choice("Postpone by", "", [1, 2, 5, 10, 15, 30], cfg.PostponeMinutes, v => $"{v} min", v => Set(s => s.Config(kind).PostponeMinutes = v)));
        page.Children.Add(Choice("Skip button after", "Hard lock never shows Skip.", [0, 3, 5, 10, 30], cfg.SkipDelaySeconds,
            v => v == 0 ? "Immediately" : $"{v} s", v => Set(s => s.Config(kind).SkipDelaySeconds = v)));
        page.Children.Add(Row($"Preview the {kind.Title().ToLowerInvariant()}", "Shows it now with the settings above.",
            Ui.Button("Preview", () => _app.Scheduler.StartBreak(kind)).With(b => b.Margin = new Thickness(0))));
    }

    private static string Duration(int seconds) => seconds < 60
        ? $"{seconds} s"
        : seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds / 60} min {seconds % 60} s";

    // Features

    private StackPanel FeaturesPage()
    {
        var page = Page();
        var f = S.Features;
        page.Children.Add(Group("How breaks reach you"));
        page.Children.Add(Toggle("Screen dimming", "Off: breaks use the floating card or a notification instead.", f.ScreenDimming, v => Set(s => s.Features.ScreenDimming = v)));
        page.Children.Add(Toggle("Full-screen overlay", "Off: soft dim becomes the strongest level.", f.FullOverlay, v => Set(s => s.Features.FullOverlay = v)));
        page.Children.Add(Toggle("Hard lock", "Off: overlays always keep a Skip button.", f.HardLock, v => Set(s => s.Features.HardLock = v)));
        page.Children.Add(Toggle("Floating card", "Off: breaks fall back to notifications.", f.FloatingCard, v => Set(s => s.Features.FloatingCard = v)));
        page.Children.Add(Toggle("Notifications", "Off: breaks show only on the tray icon.", f.Notifications, v => Set(s => s.Features.Notifications = v)));
        page.Children.Add(Toggle("Sounds", "Off: visual cues only.", f.Sounds, v => Set(s => s.Features.Sounds = v)));
        page.Children.Add(Group("Tray icon"));
        page.Children.Add(Toggle("Countdown on the tray icon", "Shows the minutes until the next break as a badge.", f.MenuBarCountdown, v => Set(s => s.Features.MenuBarCountdown = v)));
        page.Children.Add(Group("Timing"));
        page.Children.Add(Toggle("Count time away as a break", "Two minutes or more away from the keyboard resets the timer.", f.IdleCredit, v => Set(s => s.Features.IdleCredit = v)));
        page.Children.Add(Toggle("Smart pause", "Stops the timer during calls, videos and apps you pick.", f.SmartPause, v => Set(s => s.Features.SmartPause = v)));
        page.Children.Add(Toggle("Restart a dimmed break on input", "Moving the mouse or typing during a dim or overlay starts the countdown again.", f.RestartOnInput, v => Set(s => s.Features.RestartOnInput = v)));
        return page;
    }

    // Smart pause

    private StackPanel SmartPausePage()
    {
        var page = Page();
        page.Children.Add(Toggle("Smart pause", "The timer stops while any rule below matches. No permissions needed.",
            S.Features.SmartPause, v => { Set(s => s.Features.SmartPause = v); ShowPage(); }));
        if (!S.Features.SmartPause) return page;

        page.Children.Add(Group("Rules"));
        page.Children.Add(Toggle("Camera or microphone in use", "Calls and recordings.", S.SmartPause.CameraOrMic, v => Set(s => s.SmartPause.CameraOrMic = v)));
        page.Children.Add(Toggle("Video playing or screen kept awake", "Video players, presentations and full-screen apps.", S.SmartPause.VideoPlaying, v => Set(s => s.SmartPause.VideoPlaying = v)));

        page.Children.Add(Group("Apps"));
        foreach (var name in S.SmartPause.ExcludedBundleIDs.ToList())
        {
            var remove = Ui.Button("Remove", () => { Set(s => s.SmartPause.ExcludedBundleIDs.Remove(name)); ShowPage(); }).With(b => b.Margin = new Thickness(0));
            page.Children.Add(Row(name, "Pauses while this app is in front.", remove));
        }
        page.Children.Add(Row("Add an app", "Pick its .exe file.", Ui.Button("Browse…", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dialog.ShowDialog(this) != true) return;
            var name = Path.GetFileNameWithoutExtension(dialog.FileName);
            if (!S.SmartPause.ExcludedBundleIDs.Contains(name, StringComparer.OrdinalIgnoreCase))
                Set(s => s.SmartPause.ExcludedBundleIDs.Add(name));
            ShowPage();
        }).With(b => b.Margin = new Thickness(0))));
        return page;
    }

    // General

    private StackPanel GeneralPage()
    {
        var page = Page();
        page.Children.Add(Toggle("Open at login", "Start Twentea when you sign in to Windows.", SystemIntegration.LaunchAtLogin, v => SystemIntegration.LaunchAtLogin = v));

        page.Children.Add(Group("Updates"));
        var updates = new StackPanel { Orientation = Orientation.Horizontal };
        var updater = _app.Updater;
        updates.Children.Add(Ui.Button("Check now", async () => { await updater.CheckAsync(); ShowPage(); })
            .With(b => b.IsEnabled = updater.State is not (Updater.UpdateState.Checking or Updater.UpdateState.Downloading)));
        if (updater.State == Updater.UpdateState.Available)
            updates.Children.Add(Ui.Button($"Update to {updater.AvailableVersion}", () => _ = updater.InstallAsync(), "AccentButton").With(b => b.Margin = new Thickness(0)));
        page.Children.Add(Row("Twentea " + Updater.CurrentVersion, updater.StatusText, updates));

        page.Children.Add(Group("Today"));
        page.Children.Add(Row("Breaks", _app.StatsText(), new Border()));

        page.Children.Add(Group("Data"));
        page.Children.Add(Row("Break timers", "Start both countdowns from zero.", Ui.Button("Reset", () => _app.Scheduler.ResetTimers()).With(b => b.Margin = new Thickness(0))));
        page.Children.Add(Row("Data folder", TrayApp.DataDirectory, Ui.Button("Open", () => SystemIntegration.OpenFolder(TrayApp.DataDirectory)).With(b => b.Margin = new Thickness(0))));

        page.Children.Add(Group("Automation"));
        foreach (var (label, url) in new[] { ("Start a break", "twentea://break"), ("Pause 30 minutes", "twentea://pause?minutes=30"), ("Switch preset", "twentea://preset?id=night-owl"), ("Turn on or off", "twentea://toggle") })
        {
            var copy = Ui.Button("Copy", () => Clipboard.SetText(url)).With(b => b.Margin = new Thickness(0));
            page.Children.Add(Row(label, url, copy));
        }
        return page;
    }

    // Building blocks

    private static StackPanel Page() => new() { MaxWidth = 860 };

    private static TextBlock Group(string text) =>
        Ui.Text(text, 14, FontWeights.SemiBold).With(t => t.Margin = new Thickness(2, 18, 0, 8));

    private static TextBlock Note(string text) => Ui.Secondary(text).With(t => t.Margin = new Thickness(2, 8, 0, 0));

    private static Border Card(UIElement child) => new() { Style = Ui.Look("Card"), Child = child };

    /// <summary>A settings card: title and description on the left, the control on the right.</summary>
    private static Border Row(string title, string description, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(Ui.Text(title));
        if (description.Length > 0) text.Children.Add(Ui.Secondary(description).With(t => t.Margin = new Thickness(0, 2, 0, 0)));
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return Card(grid);
    }

    private static CheckBox Switch(bool value, Action<bool> set)
    {
        var box = new CheckBox { Style = Ui.Look("ToggleSwitch"), IsChecked = value };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
    }

    private static Border Toggle(string title, string description, bool value, Action<bool> set) =>
        Row(title, description, Switch(value, set));

    private static Border Choice(string title, string description, int[] options, int current, Func<int, string> text, Action<int> set)
    {
        var values = options.Contains(current) ? options.ToList() : options.Append(current).Order().ToList();
        var combo = new ComboBox { MinWidth = 180, ItemsSource = values.Select(text).ToList(), SelectedIndex = values.IndexOf(current) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set(values[combo.SelectedIndex]); };
        return Row(title, description, combo);
    }

    private string? Prompt(string title, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 10, 0, 16), MinWidth = 320 };
        var ok = Ui.Button("OK", () => { }, "AccentButton").With(b => { b.IsDefault = true; b.MinWidth = 90; });
        var cancel = Ui.Button("Cancel", () => { }).With(b => { b.IsCancel = true; b.MinWidth = 90; b.Margin = new Thickness(0); });
        var dialog = new Window
        {
            Title = "Twentea", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20),
                Children =
                {
                    Ui.Text(title, 16, FontWeights.SemiBold),
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
