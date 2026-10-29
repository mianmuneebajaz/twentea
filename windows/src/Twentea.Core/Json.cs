using System.Text.Json;
using System.Text.Json.Serialization;

namespace Twentea.Core;

/// <summary>JSON settings that produce the same keys and enum strings as the Mac app's Codable types.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Empty {typeof(T).Name}");

    public static T Clone<T>(T value) => Read<T>(Write(value));

    public static bool Same<T>(T a, T b) => Write(a) == Write(b);
}
