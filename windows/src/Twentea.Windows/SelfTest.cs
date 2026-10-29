using System.IO;
using System.Windows;
using System.Windows.Threading;
using Twentea.Core;

namespace Twentea.Windows;

/// <summary>
/// `Twentea.exe --self-test` opens the tray panel, goes from it to settings, shows every settings page
/// and every break level, then exits with 0. Any exception exits with 1. CI runs it on every build,
/// because there is no other way to exercise the WPF windows without a Windows machine.
/// </summary>
static class SelfTest
{
    private static readonly List<string> Steps = [];

    public static string LogPath => Path.Combine(TrayApp.DataDirectory, "selftest.log");

    public static async void Run(TrayApp app)
    {
        try
        {
            await Step("open tray panel", () => TrayPanel.Toggle(app));
            await Step("settings from tray panel", TrayPanel.OpenSettingsFromPanel);
            var settings = SettingsWindow.Current ?? throw new InvalidOperationException("Settings did not open");
            for (var i = 0; i < settings.PageCount; i++)
            {
                var page = i;
                await Step($"settings page {page}", () => settings.ShowPageAt(page));
            }

            app.Store.Update(s =>
            {
                s.Features.FloatingCard = s.Features.ScreenDimming = s.Features.FullOverlay = s.Features.HardLock = true;
                s.Features.Notifications = true;
            });
            foreach (var level in Enum.GetValues<Enforcement>())
            {
                await Step($"break as {level}", () =>
                {
                    app.Store.Update(s => s.Micro.Enforcement = level);
                    app.Scheduler.StartBreak(BreakKind.Micro);
                });
                await Step($"end break as {level}", app.Scheduler.ForceEnd);
            }

            await Step("toggle power", () => { app.SetOn(false); app.SetOn(true); });
            await Step("close settings", settings.Close);
            Finish(0);
        }
        catch (Exception ex)
        {
            Fail(ex, "self-test");
        }
    }

    public static void Fail(Exception ex, string where)
    {
        Steps.Add($"FAILED in {where}: {ex}");
        Finish(1);
    }

    private static async Task Step(string name, Action action)
    {
        Steps.Add(name);
        action();
        // Let layout, rendering and any queued handlers run before the next step.
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
    }

    private static void Finish(int code)
    {
        Steps.Add(code == 0 ? "PASSED" : "FAILED");
        try
        {
            Directory.CreateDirectory(TrayApp.DataDirectory);
            File.WriteAllLines(LogPath, Steps);
        }
        catch (IOException) { }
        Environment.Exit(code);
    }
}
