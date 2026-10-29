using System.Text.Json.Serialization;

namespace Twentea.Core;

/// <summary>Reads the latest GitHub release and decides whether it is newer. Mirrors mac/Sources/Twentea/Model/UpdateCheck.swift.</summary>
public static class UpdateCheck
{
    public const string LatestUrl = "https://api.github.com/repos/mianmuneebajaz/twentea/releases/latest";
    public const string ReleasesPage = "https://github.com/mianmuneebajaz/twentea/releases/latest";

    public sealed class Asset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; set; } = "";
    }

    public sealed class Release
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("assets")] public List<Asset> Assets { get; set; } = [];
    }

    /// <summary>"v1.2.3", "1.2", "1.2.3-beta.1" become [1, 2, 3]. Anything else is null.</summary>
    public static int[]? Version(string tag)
    {
        var text = tag.StartsWith('v') ? tag[1..] : tag;
        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0) text = text[..cut];
        var parts = text.Split('.');
        if (parts.Length is 0 or > 3) return null;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return null;
        return numbers;
    }

    public static bool IsNewer(string latest, string current)
    {
        if (Version(latest) is not { } l || Version(current) is not { } c) return false;
        for (var i = 0; i < 3; i++)
            if (l[i] != c[i]) return l[i] > c[i];
        return false;
    }

    public static string Display(string tag) => tag.StartsWith('v') ? tag[1..] : tag;

    /// <summary>The Windows update is the installer for this machine's architecture, for example win-x64.</summary>
    public static Asset? AssetFor(Release release, string runtimeId) =>
        release.Assets.FirstOrDefault(a => a.Name.EndsWith($"-{runtimeId}-setup.exe", StringComparison.OrdinalIgnoreCase));
}
