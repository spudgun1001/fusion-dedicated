using System.Text.Json;
using FusionDedicated;

namespace FusionDedicated.Tests.Server;

public class RconConfigTests
{
    [Fact]
    public void Rcon_is_off_by_default()
    {
        Assert.Equal("", new ServerConfig().RconPassword);
    }

    [Fact]
    public void The_default_rcon_port_is_the_source_convention()
    {
        Assert.Equal(27015, new ServerConfig().RconPort);
    }

    [Fact]
    public void Rcon_settings_survive_a_json_round_trip()
    {
        var config = new ServerConfig { RconPort = 28015, RconPassword = "hunter2" };

        var json = JsonSerializer.Serialize(config);
        var back = JsonSerializer.Deserialize<ServerConfig>(json)!;

        Assert.Equal(28015, back.RconPort);
        Assert.Equal("hunter2", back.RconPassword);
    }
}
