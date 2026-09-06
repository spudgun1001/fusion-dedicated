using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Plugins;

/// <summary>
/// What a plugin says about itself, read from plugin.json beside its DLL.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>Raised when the plugin API changes in a way that breaks plugins.</summary>
    public const int CurrentApiVersion = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("apiVersion")]
    public int ApiVersion { get; set; }

    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    public static PluginManifest? TryParse(string json, out string error)
    {
        PluginManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(json);
        }
        catch (JsonException e)
        {
            error = e.Message;
            return null;
        }

        if (manifest == null)
        {
            error = "the manifest is empty";
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            error = "the manifest has no name";
            return null;
        }

        if (string.IsNullOrWhiteSpace(manifest.Entry))
        {
            error = "the manifest has no entry";
            return null;
        }

        // An entry naming a path would load an assembly from outside the plugin's
        // own directory, which is not what dropping a folder in is meant to mean.
        if (manifest.Entry.Contains('/') || manifest.Entry.Contains('\\')
            || manifest.Entry.Contains(".."))
        {
            error = "the entry must be a file name, not a path";
            return null;
        }

        if (manifest.ApiVersion != CurrentApiVersion)
        {
            error = $"the plugin is built for API version {manifest.ApiVersion}, "
                  + $"and this server offers {CurrentApiVersion}";
            return null;
        }

        error = "";
        return manifest;
    }
}
