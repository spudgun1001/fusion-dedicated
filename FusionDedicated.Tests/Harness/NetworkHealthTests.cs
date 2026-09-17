using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Players lagged late in long sessions and nothing showed whether it was their network.
/// Each player's connection is logged once a minute, and a bad one is a warning.
/// </summary>
public class NetworkHealthTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    private static readonly ConnectionHealth Good = new(PingMs: 85, QualityLocal: 0.99f, QualityRemote: 0.97f,
        OutBytesPerSecond: 42_000f, PendingBytes: 1_100, QueueMicroseconds: 3_000);

    private static List<ServerLogEntry> NetLines(World world)
        => world.Server.RecentLog(2000).Where(e => e.Message.StartsWith("Net ")).ToList();

    [Fact]
    public void Each_player_gets_one_line_a_minute()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        world.Transport.SetHealth(joel.Connection, Good);
        world.Transport.SetHealth(kanza.Connection, Good);

        world.Tick();
        world.Tick();

        Assert.Equal(2, NetLines(world).Count);

        world.Advance(TimeSpan.FromMinutes(1));
        world.Tick();

        Assert.Equal(4, NetLines(world).Count);
    }

    [Fact]
    public void A_good_connection_is_info_and_says_what_was_measured()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        world.Transport.SetHealth(joel.Connection, Good);

        world.Tick();

        var line = Assert.Single(NetLines(world));
        Assert.Equal("INFO", line.Level);
        Assert.Equal("Net Joel: ping 85 ms, quality 99%/97%, out 42 KB/s, waiting 1.1 KB, queue 3 ms", line.Message);
    }

    [Theory]
    [InlineData(300, 0.99f, 0L)]
    [InlineData(80, 0.85f, 0L)]
    [InlineData(80, 0.99f, 600_000L)]
    public void A_bad_connection_is_a_warning(int ping, float quality, long queueMicroseconds)
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        world.Transport.SetHealth(joel.Connection, Good with
        {
            PingMs = ping,
            QualityRemote = quality,
            QueueMicroseconds = queueMicroseconds,
        });

        world.Tick();

        Assert.Equal("WARN", Assert.Single(NetLines(world)).Level);
    }

    [Fact]
    public void A_player_Steam_has_no_status_for_gets_no_line()
    {
        using var world = new World();
        world.Join(JoelId, "Joel");

        world.Tick();

        Assert.Empty(NetLines(world));
    }
}
