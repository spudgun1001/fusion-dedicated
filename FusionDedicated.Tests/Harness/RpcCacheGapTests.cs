using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>The RPC cache remembers only values players were actually sent, so a later matching broadcast is never wrongly skipped.</summary>
public class RpcCacheGapTests
{
    private static readonly string Path = RpcProtocol.PathFor(300, 0);
    private static readonly byte[] PathBytes = Convert.FromHexString(Path);

    private static byte[] ClientString(FakePlayer from, string value)
        => GateProtocol.BuildRpcVariable((byte)RpcKind.String, from.SmallId, from.SmallId,
            RpcProtocol.WriteValue(RpcKind.String, PathBytes, RpcValue.OfString(value)));

    private static int RpcCountTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(m => m.Message[0] is >= 209 and <= 214);

    private static void DropEverything(World world)
    {
        var rpc = new PluginRpc(new PluginHealth(), (_, _) => { });
        rpc.Watch("test", _ => RpcAction.Drop);
        world.Server.PluginRpc = rpc;
    }

    [Fact]
    public void A_value_a_plugin_dropped_is_not_replayed_to_a_joiner()
    {
        using var world = new World();
        var author = world.Join(1, "A");
        author.FinishLoading();
        DropEverything(world);

        author.Send(ClientString(author, "dropped"));

        var joiner = world.Join(2, "B");
        joiner.FinishLoading();

        Assert.Equal(0, RpcCountTo(world, joiner, 0));
    }

    [Fact]
    public void After_a_dropped_value_a_matching_broadcast_is_still_sent()
    {
        using var world = new World();
        var author = world.Join(1, "A");
        var other = world.Join(2, "B");
        author.FinishLoading();
        other.FinishLoading();
        DropEverything(world);

        author.Send(ClientString(author, "412"));
        int before = world.Transport.SentTo(other.Connection).Count;

        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("412"), null);

        Assert.Equal(1, RpcCountTo(world, other, before));
    }

    [Fact]
    public void After_an_oversized_client_value_a_matching_broadcast_is_still_sent()
    {
        using var world = new World();
        var author = world.Join(1, "A");
        var other = world.Join(2, "B");
        author.FinishLoading();
        other.FinishLoading();

        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("small"), null);
        author.Send(ClientString(author, new string('Y', 600)));
        int before = world.Transport.SentTo(other.Connection).Count;

        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("small"), null);

        Assert.Equal(1, RpcCountTo(world, other, before));
    }

    [Fact]
    public void After_an_oversized_broadcast_a_matching_small_broadcast_is_still_sent()
    {
        using var world = new World();
        var player = world.Join(1, "A");
        player.FinishLoading();

        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("small"), null);
        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString(new string('Y', 600)), null);
        int before = world.Transport.SentTo(player.Connection).Count;

        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("small"), null);

        Assert.Equal(1, RpcCountTo(world, player, before));
    }
}

/// <summary>Forgetting one cached variable.</summary>
public class RpcVariableCacheForgetTests
{
    [Fact]
    public void Forgetting_a_variable_removes_only_that_one()
    {
        var cache = new RpcVariableCache();
        byte[] first = Convert.FromHexString(RpcProtocol.PathFor(300, 0));
        byte[] second = Convert.FromHexString(RpcProtocol.PathFor(300, 1));

        cache.Set(213, 1, new byte[] { 1 }, first);
        cache.Set(213, 1, new byte[] { 2 }, second);

        Assert.True(cache.Forget(213, first));
        Assert.False(cache.IsUnchanged(213, new byte[] { 1 }, first));
        Assert.True(cache.IsUnchanged(213, new byte[] { 2 }, second));
        Assert.False(cache.Forget(213, first));
    }
}
