using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// LogRpc: one line per RPC message for chasing why a level's own tags (209-214) never
/// reached a plugin, since the ordinary logs stay silent on a message that was read but
/// answered nobody.
/// </summary>
public class RpcDiagnosticLogTests
{
    private const ulong JoelId = 76561198000000001;
    private static readonly string Hash = LevelRpc.Hash(1);

    private static ServerConfig Config(bool logRpc, int rpcPerSecond = 250) => new()
    {
        CullOrphanedEntities = false,
        LogRpc = logRpc,
        RpcMessagesPerSecond = rpcPerSecond,
    };

    private static FakePlayer Loaded(World world)
    {
        var player = world.Join(JoelId, "Joel");
        player.FinishLoading();
        return player;
    }

    private static PluginRpc Watching(World world, Func<RpcRequest, RpcAction> handler)
    {
        var rpc = new PluginRpc(new PluginHealth(), (_, _) => { });
        rpc.Watch("test", handler);
        world.Server.PluginRpc = rpc;
        return rpc;
    }

    private static List<string> RpcLines(World world)
        => world.Server.RecentLog(2000).Where(e => e.Message.StartsWith("RPC from")).Select(e => e.Message).ToList();

    [Fact]
    public void Nothing_is_logged_while_LogRpc_is_off()
    {
        using var world = new World(Config(logRpc: false));
        var joel = Loaded(world);
        Watching(world, _ => RpcAction.Pass);

        joel.Send(LevelRpc.Int(joel.SmallId, Hash, 0, 1));

        Assert.Empty(RpcLines(world));
    }

    [Fact]
    public void A_message_dropped_by_the_rpc_budget_is_logged()
    {
        using var world = new World(Config(logRpc: true, rpcPerSecond: 1));
        var joel = Loaded(world);

        joel.Send(LevelRpc.Int(joel.SmallId, Hash, 0, 1));
        joel.Send(LevelRpc.Int(joel.SmallId, Hash, 1, 2));

        var line = Assert.Single(RpcLines(world));
        Assert.Contains("RPC from Joel", line);
        Assert.Contains("dropped by budget", line);
    }

    [Fact]
    public void A_path_too_short_to_read_is_logged()
    {
        using var world = new World(Config(logRpc: true));
        var joel = Loaded(world);
        Watching(world, _ => RpcAction.Pass);

        // ToTarget at itself, the way RpcCacheGapTests builds a client value, but with
        // a body too short to hold a path (needs 6 bytes, this has 3).
        joel.Send(GateProtocol.BuildRpcVariable((byte)RpcKind.Int, joel.SmallId, joel.SmallId, new byte[] { 1, 2, 3 }));

        var line = Assert.Single(RpcLines(world));
        Assert.Contains("path unreadable", line);
    }

    [Fact]
    public void An_unreadable_body_is_logged()
    {
        using var world = new World(Config(logRpc: true));
        var joel = Loaded(world);
        Watching(world, _ => RpcAction.Pass);
        var sender = world.Server.Players.GetByPlatformId(JoelId)!;

        // Too short to hold even the tag/relay/channel prefix GateProtocol reads.
        var result = world.Server.OfferRpcToPlugins(sender, (byte)RpcKind.Int, new byte[] { 1, 2 });

        Assert.Equal(RpcActionKind.Pass, result);
        var line = Assert.Single(RpcLines(world));
        Assert.Contains("body unreadable", line);
    }

    [Theory]
    [InlineData(true, "offered: Drop")]
    [InlineData(false, "offered: Pass")]
    public void An_rpc_offered_to_a_watching_plugin_is_logged_with_its_path(bool drop, string outcome)
    {
        using var world = new World(Config(logRpc: true));
        var joel = Loaded(world);
        Watching(world, _ => drop ? RpcAction.Drop : RpcAction.Pass);

        joel.Send(LevelRpc.Bool(joel.SmallId, Hash, 7, true));

        var line = Assert.Single(RpcLines(world));
        Assert.Contains(outcome, line);
        Assert.Contains("HasEntity=False", line);
        Assert.Contains($"hash={LevelRpc.Path(Hash, 7)}", line);
    }

    [Fact]
    public void Logging_is_capped_at_30_lines_a_second()
    {
        using var world = new World(Config(logRpc: true, rpcPerSecond: 0));
        var joel = Loaded(world);
        Watching(world, _ => RpcAction.Pass);

        for (ushort i = 0; i < 40; i++)
        {
            joel.Send(LevelRpc.Bool(joel.SmallId, Hash, i, true));
        }

        Assert.Equal(30, RpcLines(world).Count);
    }
}
