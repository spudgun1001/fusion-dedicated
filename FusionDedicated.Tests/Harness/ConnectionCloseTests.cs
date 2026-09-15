using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Tests.Server;
using Steamworks;

namespace FusionDedicated.Tests.Harness;

/// <summary>Refused joins, silent connections and kicks all close their connection on the main loop.</summary>
public class ConnectionCloseTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    [Fact]
    public void A_join_refused_for_a_version_mismatch_is_closed_a_quarter_second_later()
    {
        using var world = new World();
        var connection = world.Transport.Connect();

        world.Transport.Deliver(connection, OutdatedJoin(KanzaId));
        world.Server.Receive();

        Assert.Equal("Version mismatch: server is v1.14", RefusalSentTo(world, connection));
        AssertClosedAfterTheDelay(world, connection);
    }

    [Fact]
    public void A_join_refused_for_a_ban_is_closed_a_quarter_second_later()
    {
        var config = new ServerConfig { CullOrphanedEntities = false };
        config.Bans.Add(new BanEntry { PlatformId = KanzaId, Reason = "Griefing" });
        using var world = new World(config);

        var connection = Ask(world, KanzaId, "Kanza");

        Assert.Equal("Griefing", RefusalSentTo(world, connection));
        AssertClosedAfterTheDelay(world, connection);
    }

    [Fact]
    public void A_join_refused_because_the_server_filled_up_is_closed_a_quarter_second_later()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, MaxPlayers = 1 });
        var late = world.Transport.Connect();

        world.Join(JoelId, "Joel");

        world.Transport.Deliver(late, ClientMessages.Join(world.Server.Config, KanzaId, "Kanza"));
        world.Server.Receive();

        Assert.Equal("Server is full! Wait for someone to leave.", RefusalSentTo(world, late));
        AssertClosedAfterTheDelay(world, late);
    }

    [Fact]
    public void A_second_join_from_somebody_already_here_is_closed_a_quarter_second_later()
    {
        using var world = new World();
        world.Join(JoelId, "Joel");

        var second = Ask(world, JoelId, "Joel");

        Assert.Equal("You attempted to join, but the server detects you as already in it?", RefusalSentTo(world, second));
        AssertClosedAfterTheDelay(world, second);
    }

    [Fact]
    public void A_join_a_plugin_refuses_is_closed_a_quarter_second_later()
    {
        using var world = new World();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Joining.Subscribe("gate", _ => PluginVerdict.Refuse("not invited"));
        world.Server.Plugins = events;

        var connection = Ask(world, KanzaId, "Kanza");

        Assert.Equal("not invited", RefusalSentTo(world, connection));
        AssertClosedAfterTheDelay(world, connection);
    }

    [Fact]
    public void A_retry_on_a_refused_connection_is_ignored_until_it_closes()
    {
        using var world = new World();
        var connection = world.Transport.Connect();

        world.Transport.Deliver(connection, OutdatedJoin(KanzaId));
        world.Server.Receive();
        world.Transport.Deliver(connection, OutdatedJoin(KanzaId));
        world.Server.Receive();

        Assert.Single(world.Server.RecentLog(2000), e => e.Message.StartsWith($"Rejected {KanzaId}:"));
    }

    [Fact]
    public void A_joined_player_who_asks_again_keeps_their_connection()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");

        world.Transport.Deliver(joel.Connection, ClientMessages.Join(world.Server.Config, JoelId, "Joel"));
        world.Server.Receive();
        world.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(world.Transport.Closed);
        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));
    }

    [Fact]
    public void A_connection_that_never_asks_to_join_is_closed_after_fifteen_seconds()
    {
        using var world = new World();
        var connection = world.Transport.Connect();

        world.Advance(TimeSpan.FromSeconds(14));

        Assert.Empty(world.Transport.Closed);

        world.Advance(TimeSpan.FromSeconds(1));

        var closed = Assert.Single(world.Transport.Closed);
        Assert.Equal(connection.m_HSteamNetConnection, closed.Connection);
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message.StartsWith("Closed a connection that never asked to join"));
    }

    [Fact]
    public void A_connection_that_joins_in_time_is_not_closed()
    {
        using var world = new World();
        world.Join(JoelId, "Joel");

        world.Advance(TimeSpan.FromSeconds(20));

        Assert.Empty(world.Transport.Closed);
        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));
    }

    [Fact]
    public void A_connection_that_closes_before_asking_is_forgotten()
    {
        using var world = new World();
        var connection = world.Transport.Connect();

        world.Transport.Disconnect(connection, "Closing Connection");
        world.Advance(TimeSpan.FromSeconds(15));

        Assert.Empty(world.Transport.Closed);
        Assert.DoesNotContain(world.Server.RecentLog(2000),
            e => e.Message.StartsWith("Closed a connection that never asked to join"));
    }

    [Fact]
    public void A_kick_closes_the_connection_on_the_main_loop_a_quarter_second_later()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");

        world.Server.Kick(joel.SmallId, "test");

        Assert.Empty(world.Transport.Closed);

        world.Advance(TimeSpan.FromMilliseconds(250));

        var closed = Assert.Single(world.Transport.Closed);
        Assert.Equal(joel.Connection.m_HSteamNetConnection, closed.Connection);
        Assert.Equal("test", closed.Reason);
    }

    [Fact]
    public void Kick_no_longer_closes_on_a_thread_pool_timer()
    {
        string kick = FusionServerSource.Method("public void Kick(");

        Assert.DoesNotContain("Task.Delay(", kick);
        Assert.Contains("CloseSoon(connection, reason);", kick);
    }

    [Fact]
    public void The_shutdown_runs_the_kick_closes_after_its_wait()
    {
        string afterWait = ProgramSource.Between("await Task.Delay(400);", "SteamAPI.Shutdown();");

        Assert.Contains("server.Exclusive(() => server.PumpDeferred());", afterWait);
    }

    internal static HSteamNetConnection Ask(World world, ulong platformId, string name)
    {
        var connection = world.Transport.Connect();
        world.Transport.Deliver(connection, ClientMessages.Join(world.Server.Config, platformId, name));
        world.Server.Receive();
        return connection;
    }

    /// <summary>The reason in the last disconnect message sent down this connection.</summary>
    internal static string? RefusalSentTo(World world, HSteamNetConnection connection)
        => world.Transport.SentTo(connection)
            .Select(sent => Envelope.Read(sent.Message))
            .OfType<Envelope>()
            .Where(envelope => envelope.Tag == FusionProtocol.TagDisconnect)
            .Select(envelope => ReasonOf(envelope.Payload))
            .LastOrDefault();

    private static string? ReasonOf(byte[] payload)
    {
        var reader = new FusionNetReader(payload);
        reader.ReadUInt64();
        return reader.ReadString();
    }

    private static byte[] OutdatedJoin(ulong platformId)
        => FusionProtocol.BuildConnectionRequest(
            platformId, new System.Version(1, 0, 0), "SLZ.BONELAB.Content.Avatar.FordBW",
            new Dictionary<string, string> { ["Username"] = "Kanza" }, new List<string>());

    private static void AssertClosedAfterTheDelay(World world, HSteamNetConnection connection)
    {
        Assert.DoesNotContain(world.Transport.Closed, c => c.Connection == connection.m_HSteamNetConnection);

        world.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Contains(world.Transport.Closed, c => c.Connection == connection.m_HSteamNetConnection);
    }
}
