using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using Twentea.Core;

namespace Twentea.Windows;

/// <summary>
/// Checks GitHub releases on launch and every six hours. Update downloads the installer for this
/// machine, runs it silently and quits; the installer replaces the app and starts it again.
/// A portable copy (not under the install folder) opens the releases page instead.
/// </summary>
sealed class Updater
{
    public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Failed }

    private static readonly HttpClient Http = CreateClient();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly Action _quit;
    private UpdateCheck.Release? _release;

    public UpdateState State { get; private set; }
    public string? AvailableVersion { get; private set; }
    public event Action? Changed;

    public Updater(Action quit) => _quit = quit;

    public static string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private static string RuntimeId =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    /// <summary>True when the setup program installed this copy, so its installer can replace it.</summary>
    private static bool IsInstalled =>
        Environment.ProcessPath is { } path &&
        path.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Twentea"),
                        StringComparison.OrdinalIgnoreCase);

    public string StatusText => State switch
    {
        UpdateState.Checking => "Checking…",
        UpdateState.UpToDate => $"Twentea {CurrentVersion} is the latest version",
        UpdateState.Available => $"Twentea {AvailableVersion} is available",
        UpdateState.Downloading => "Downloading the update…",
        UpdateState.Failed => "Could not check for updates",
        _ => "Not checked yet",
    };

    public void Start()
    {
        _ = CheckAsync();
        _timer.Tick += (_, _) => _ = CheckAsync();
        _timer.Start();
    }

    public async Task CheckAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading) return;
        Set(UpdateState.Checking);
        try
        {
            using var response = await Http.GetAsync(UpdateCheck.LatestUrl);
            // 404 means the repository has no published release yet.
            if (response.StatusCode == HttpStatusCode.NotFound) { Set(UpdateState.UpToDate); return; }
            response.EnsureSuccessStatusCode();
            var release = JsonSerializer.Deserialize<UpdateCheck.Release>(await response.Content.ReadAsStringAsync())
                ?? throw new JsonException("Empty release");
            _release = release;
            if (UpdateCheck.IsNewer(release.TagName, CurrentVersion))
            {
                AvailableVersion = UpdateCheck.Display(release.TagName);
                Set(UpdateState.Available);
            }
            else
            {
                Set(UpdateState.UpToDate);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            Set(UpdateState.Failed);
        }
    }

    public async Task InstallAsync()
    {
        if (_release is null || UpdateCheck.AssetFor(_release, RuntimeId) is not { } asset || !IsInstalled)
        {
            OpenReleasePage();
            return;
        }
        Set(UpdateState.Downloading);
        try
        {
            var file = Path.Combine(Path.GetTempPath(), asset.Name);
            await using (var download = await Http.GetStreamAsync(asset.BrowserDownloadUrl))
            await using (var output = File.Create(file))
                await download.CopyToAsync(output);
            Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            _quit();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            Set(UpdateState.Failed);
        }
    }

    public static void OpenReleasePage() =>
        Process.Start(new ProcessStartInfo(UpdateCheck.ReleasesPage) { UseShellExecute = true });

    private void Set(UpdateState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Twentea/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
