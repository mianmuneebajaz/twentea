using System.Text.Json;

namespace Twentea.Core;

public sealed class Preset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>SF Symbol name from the Mac app. The Windows UI maps it to its own glyph.</summary>
    public string Symbol { get; set; } = "";
    public bool BuiltIn { get; set; }
    public AppSettings Settings { get; set; } = new();
}

public static class Presets
{
    public const string DefaultId = "balanced";

    /// <summary>Loaded from shared/presets.json, the same file the Mac tests check against.</summary>
    public static IReadOnlyList<Preset> BuiltIns { get; } = Load();

    private static List<Preset> Load()
    {
        using var stream = typeof(Presets).Assembly.GetManifestResourceStream("presets.json")
            ?? throw new InvalidOperationException("presets.json is not embedded");
        return JsonSerializer.Deserialize<List<Preset>>(stream, Json.Options)
            ?? throw new InvalidOperationException("presets.json is empty");
    }
}
