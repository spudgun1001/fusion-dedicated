using FusionDedicated;
using Steamworks;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Steam sends to each connection at 256 KB/s and holds 512 KB unless told otherwise, and every
/// player sat at that rate with half a megabyte waiting. The server now sets both at startup.
/// </summary>
public class SteamSendLimitsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fd-send-" + Guid.NewGuid());

    public SteamSendLimitsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_defaults_raise_the_rate_and_the_buffer()
    {
        var limits = SteamSendLimits.From(new ServerConfig());

        Assert.Equal(new[]
        {
            (ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, 1048576),
            (ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, 1048576),
            (ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, 2097152),
        }, limits);
    }

    [Fact]
    public void A_value_of_zero_leaves_Steams_own_default()
    {
        var limits = SteamSendLimits.From(new ServerConfig { SendRateMin = 0, SendRateMax = 0 });

        Assert.Equal(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, Assert.Single(limits).Item1);
    }

    [Fact]
    public void The_keys_are_read_from_the_config_file()
    {
        string path = Path.Combine(_dir, "server.json");
        File.WriteAllText(path, """{"SendRateMin":524288,"SendRateMax":2097152,"SendBufferSize":4194304,"VoiceRelayRange":60}""");

        var config = ServerConfig.Load(path, out string? error);

        Assert.Null(error);
        Assert.Equal(new[] { 524288, 2097152, 4194304 }, SteamSendLimits.From(config).Select(l => l.Item2));
        Assert.Equal(60f, config.VoiceRelayRange);
    }

    [Fact]
    public void The_startup_line_names_each_value()
        => Assert.Equal("Steam send limits: rate 1024-1024 KB/s, buffer 2048 KB",
            SteamSendLimits.Describe(new ServerConfig()));
}
