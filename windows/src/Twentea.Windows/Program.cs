using System.Windows;
using Forms = System.Windows.Forms;

namespace Twentea.Windows;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\Twentea.SingleInstance", out var first);
        if (!first)
        {
            // A second launch opens settings, or passes its twentea:// URL to the running app.
            SystemIntegration.Forward(args.FirstOrDefault() ?? "twentea://settings");
            return;
        }

        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        SystemIntegration.RegisterUrlScheme();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // The Fluent theme gives every window the Windows 11 look and follows light and dark mode.
#pragma warning disable WPF0001
        app.ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Twentea;component/Theme.xaml") });
        var tray = new TrayApp();
        _ = SystemIntegration.ListenAsync(url => app.Dispatcher.InvokeAsync(() => tray.Handle(url)));
        foreach (var url in args) tray.Handle(url);
        app.Run();
    }
}
