using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginManifestTests
{
    private const string Valid = """
        { "name": "police", "version": "1.0.0", "apiVersion": 1, "entry": "Police.dll" }
        """;

    [Fact]
    public void A_valid_manifest_parses()
    {
        var manifest = PluginManifest.TryParse(Valid, out string error);

        Assert.NotNull(manifest);
        Assert.Equal("", error);
        Assert.Equal("police", manifest!.Name);
        Assert.Equal("Police.dll", manifest.Entry);
    }

    [Fact]
    public void A_manifest_for_another_api_version_is_refused_by_number()
    {
        var manifest = PluginManifest.TryParse(
            """{ "name": "x", "version": "1", "apiVersion": 2, "entry": "X.dll" }""",
            out string error);

        Assert.Null(manifest);
        Assert.Contains("2", error);
        Assert.Contains("1", error);
    }

    [Fact]
    public void A_manifest_with_no_name_is_refused()
    {
        Assert.Null(PluginManifest.TryParse(
            """{ "version": "1", "apiVersion": 1, "entry": "X.dll" }""", out _));
    }

    [Fact]
    public void A_manifest_with_no_entry_is_refused()
    {
        Assert.Null(PluginManifest.TryParse(
            """{ "name": "x", "version": "1", "apiVersion": 1 }""", out _));
    }

    [Fact]
    public void Rubbish_is_refused_rather_than_throwing()
    {
        Assert.Null(PluginManifest.TryParse("{ not json", out string error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void An_entry_that_escapes_its_directory_is_refused()
    {
        // A manifest naming ../../something would load an assembly from outside
        // the plugin's own folder.
        Assert.Null(PluginManifest.TryParse(
            """{ "name": "x", "version": "1", "apiVersion": 1, "entry": "../evil.dll" }""",
            out _));
    }
}
