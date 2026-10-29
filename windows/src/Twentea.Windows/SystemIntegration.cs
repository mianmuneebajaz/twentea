using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Microsoft.Win32;

namespace Twentea.Windows;

/// <summary>Launch at login, the twentea:// scheme and single-instance forwarding. All per user, no admin rights.</summary>
static class SystemIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string PipeName = $"Twentea.{Environment.UserName}.{Process.GetCurrentProcess().SessionId}";

    private static string ExePath => Environment.ProcessPath ?? "Twentea.exe";

    public static bool LaunchAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Twentea") is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Twentea", $"\"{ExePath}\"");
            else key.DeleteValue("Twentea", throwOnMissingValue: false);
        }
    }

    /// <summary>Points twentea:// at this exe. Runs on every launch so a moved exe stays registered.</summary>
    public static void RegisterUrlScheme()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\twentea");
        key.SetValue("", "URL:Twentea");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{ExePath}\",0");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{ExePath}\" \"%1\"");
    }

    /// <summary>Sends a twentea:// URL to the instance that is already running.</summary>
    public static void Forward(string url)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client);
            writer.WriteLine(url);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { }
    }

    /// <summary>Receives URLs from later launches, one per connection.</summary>
    public static async Task ListenAsync(Action<string> handle)
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync() is { Length: > 0 } line) handle(line);
            }
            catch (IOException)
            {
                await Task.Delay(500);
            }
        }
    }

    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
}
