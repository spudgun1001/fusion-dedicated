using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Safety;

public sealed class GlobalModEntry
{
    [JsonPropertyName("barcodes")]
    public List<string> Barcodes { get; set; } = new();

    [JsonPropertyName("modID")]
    public int ModId { get; set; } = -1;

    [JsonPropertyName("nameID")]
    public string NameId { get; set; } = "";
}

public sealed class GlobalModBlacklist
{
    [JsonPropertyName("mods")]
    public List<GlobalModEntry> Mods { get; set; } = new();
}

public sealed class GlobalBanPlatform
{
    [JsonPropertyName("platformID")]
    public ulong PlatformId { get; set; }

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "";
}

public sealed class GlobalBanEntry
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("platforms")]
    public List<GlobalBanPlatform> Platforms { get; set; } = new();
}

public sealed class GlobalBanList
{
    [JsonPropertyName("bans")]
    public List<GlobalBanEntry> Bans { get; set; } = new();
}

/// <summary>
/// Reads the community lists Fusion publishes at
/// github.com/Lakatrazz/Fusion-Lists. Returns null rather than throwing, so a
/// corrupt download can be discarded in favour of the cache.
/// </summary>
public static class SafetyListParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static GlobalModBlacklist? ParseModBlacklist(string json) => Parse<GlobalModBlacklist>(json);

    public static GlobalBanList? ParseBanList(string json) => Parse<GlobalBanList>(json);

    private static T? Parse<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
