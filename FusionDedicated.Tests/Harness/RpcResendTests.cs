using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>SendRpc should not resend a broadcast value the cache already holds.</summary>
public class RpcResendTests
{
    private static readonly string Path = RpcProtocol.PathFor(300, 0);

    private static int RpcCountTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(m => m.Message[0] is >= 209 and <= 214);

    [Fact]
    public void A_broadcast_repeating_the_cached_value_is_not_resent()
    {
        using var world = new World();
        var players = new[] { world.Join(1, "A"), world.Join(2, "B") };
        players.ToList().ForEach(p => p.FinishLoading());

        var target = players[0];
        var before = world.Transport.SentTo(target.Connection).Count;

        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("412"), null);
        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("412"), null);
        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("413"), null);

        Assert.Equal(2, RpcCountTo(world, target, before));
    }

    [Fact]
    public void A_per_player_send_repeating_the_same_value_is_sent_every_time()
    {
        using var world = new World();
        var player = world.Join(1, "A");
        player.FinishLoading();

        var before = world.Transport.SentTo(player.Connection).Count;

        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("412"), 1);
        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString("412"), 1);

        Assert.Equal(2, RpcCountTo(world, player, before));
    }
}
