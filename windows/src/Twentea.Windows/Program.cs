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
        var tray = new TrayApp();
        _ = SystemIntegration.ListenAsync(url => app.Dispatcher.InvokeAsync(() => tray.Handle(url)));
        foreach (var url in args) tray.Handle(url);
        app.Run();
    }
}
