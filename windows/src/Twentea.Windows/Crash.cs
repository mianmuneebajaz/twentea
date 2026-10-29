using System.IO;

namespace Twentea.Windows;

/// <summary>Writes unexpected errors to crash.log in the data folder, so a failure leaves something to read.</summary>
static class Crash
{
    public static string LogPath => Path.Combine(TrayApp.DataDirectory, "crash.log");

    public static void Log(Exception? ex, string where)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(TrayApp.DataDirectory);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {where} (Twentea {Updater.CurrentVersion})\n{ex}\n\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
