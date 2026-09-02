using FusionDedicated;

namespace FusionDedicated.Tests.Server;

public class ConfigLoadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-cfg-" + Guid.NewGuid());

    public ConfigLoadTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string json)
    {
        string path = Path.Combine(_dir, "server.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"false\"", false)]
    [InlineData("\"1\"", true)]
    [InlineData("\"0\"", false)]
    public void A_boolean_survives_however_the_panel_spelled_it(string raw, bool expected)
    {
        var config = ServerConfig.Load(
            Write($$"""{"ServerName":"Kept","ExtendedProtection":{{raw}}}"""), out string? error);

        Assert.Null(error);
        Assert.Equal("Kept", config.ServerName);
        Assert.Equal(expected, config.ExtendedProtection);
    }

    [Fact]
    public void An_unparsable_value_is_reported_and_the_file_is_left_alone()
    {
        // Salvaging the readable half would need a bespoke lenient parser. What
        // matters is that the operator is told and their file is not overwritten,
        // so nothing is lost permanently.
        const string original = """{"ServerName":"Kept","ExtendedProtection":"yes please"}""";
        string path = Write(original);

        ServerConfig.Load(path, out string? error);

        Assert.NotNull(error);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void Malformed_json_reports_an_error_rather_than_failing_silently()
    {
        var config = ServerConfig.Load(Write("{ not json"), out string? error);

        Assert.NotNull(error);
        Assert.Equal(new ServerConfig().ServerName, config.ServerName);
    }

    [Fact]
    public void A_good_file_reports_no_error()
    {
        var config = ServerConfig.Load(
            Write("""{"ServerName":"Badger","MaxPlayers":24}"""), out string? error);

        Assert.Null(error);
        Assert.Equal("Badger", config.ServerName);
        Assert.Equal(24, config.MaxPlayers);
    }

    [Fact]
    public void An_absent_file_is_not_an_error()
    {
        var config = ServerConfig.Load(Path.Combine(_dir, "missing.json"), out string? error);

        Assert.Null(error);
        Assert.NotNull(config);
    }
}
