using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>What a player who left, or a level that changed, must not leave behind.</summary>
public class LeftoverStateTests
{
    private const string Avatar = "Pack.Avatar.Robot";

    private static int RpcCountTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection).Count(m => m.Message[0] is >= 209 and <= 214);

    private static byte[] ClientString(FakePlayer from, string path, string value)
        => GateProtocol.BuildRpcVariable((byte)RpcKind.String, from.SmallId, from.SmallId,
            RpcProtocol.WriteValue(RpcKind.String, Convert.FromHexString(path), RpcValue.OfString(value)));

    /// <summary>An avatar swap as a client sends it: the proportions block, then the barcode.</summary>
    private static byte[] AvatarSwap(FakePlayer player, string barcode)
    {
        var payload = new FusionNetWriter(FusionProtocol.AvatarStatsSize + 64);
        payload.WriteRaw(new byte[FusionProtocol.AvatarStatsSize]);
        payload.Write(barcode);

        var message = new FusionNetWriter(FusionProtocol.AvatarStatsSize + 96);
        message.Write(GateProtocol.TagPlayerRepAvatar);
        message.Write((byte)3);     // ToOtherClients
        message.Write((byte)0);     // reliable
        message.WriteNullable(player.SmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    private static int OversizedLines(World world, string path)
        => world.Server.RecentLog(2000).Count(e =>
            e.Message.StartsWith("Not keeping an RPC value of", StringComparison.Ordinal)
            && e.Message.Contains($" for {path},", StringComparison.Ordinal));

    [Fact]
    public void A_rig_variable_is_not_replayed_to_the_next_player_given_that_small_id()
    {
        using var world = new World();
        var leaver = world.Join(1, "A");
        leaver.FinishLoading();
        leaver.Send(ClientString(leaver, RpcProtocol.PathFor(leaver.SmallId, 0), "worn"));
        world.Leave(leaver, "Closing Connection");

        var next = world.Join(2, "B");
        next.FinishLoading();

        Assert.Equal((byte)1, next.SmallId);
        Assert.Equal(0, RpcCountTo(world, next));
    }

    [Fact]
    public void A_prop_variable_set_by_a_player_who_left_is_still_replayed()
    {
        using var world = new World();
        var leaver = world.Join(1, "A");
        leaver.FinishLoading();
        leaver.Send(ClientString(leaver, RpcProtocol.PathFor(300, 0), "open"));
        world.Leave(leaver, "Closing Connection");

        var next = world.Join(2, "B");
        next.FinishLoading();

        Assert.Equal(1, RpcCountTo(world, next));
    }

    [Fact]
    public void A_barcode_is_forgotten_when_its_only_holder_leaves()
    {
        using var world = new World();
        var wearer = world.Join(1, "A");
        wearer.FinishLoading();
        wearer.Send(AvatarSwap(wearer, Avatar));
        Assert.Equal(1, world.Server.BarcodesWithHolders);

        world.Leave(wearer, "Closing Connection");

        Assert.Equal(0, world.Server.BarcodesWithHolders);
    }

    [Fact]
    public void A_barcode_another_player_still_holds_is_kept_when_one_leaves()
    {
        using var world = new World();
        var first = world.Join(1, "A");
        var second = world.Join(2, "B");
        first.FinishLoading();
        second.FinishLoading();
        first.Send(AvatarSwap(first, Avatar));
        second.Send(AvatarSwap(second, Avatar));

        world.Leave(first, "Closing Connection");

        Assert.Equal(1, world.Server.BarcodesWithHolders);
    }

    [Fact]
    public void Barcode_holders_are_forgotten_on_a_level_change()
    {
        using var world = new World();
        var wearer = world.Join(1, "A");
        wearer.FinishLoading();
        wearer.Send(AvatarSwap(wearer, Avatar));
        Assert.Equal(1, world.Server.BarcodesWithHolders);

        world.Server.SetLevel("Pack.Level.Other", "Other", 0, null);

        Assert.Equal(0, world.Server.BarcodesWithHolders);
    }

    [Fact]
    public void An_oversized_value_is_logged_once_for_its_path()
    {
        using var world = new World();
        var author = world.Join(1, "A");
        author.FinishLoading();
        string path = RpcProtocol.PathFor(300, 0);

        author.Send(ClientString(author, path, new string('Y', 600)));
        author.Send(ClientString(author, path, new string('Z', 600)));
        world.Server.SendRpc(RpcKind.String, path, RpcValue.OfString(new string('Y', 600)), null);

        Assert.Equal(1, OversizedLines(world, path));
    }

    [Fact]
    public void Each_oversized_path_is_logged_on_its_own()
    {
        using var world = new World();
        var player = world.Join(1, "A");
        player.FinishLoading();
        string first = RpcProtocol.PathFor(300, 0);
        string second = RpcProtocol.PathFor(301, 0);

        world.Server.SendRpc(RpcKind.String, first, RpcValue.OfString(new string('Y', 600)), null);
        world.Server.SendRpc(RpcKind.String, second, RpcValue.OfString(new string('Y', 600)), null);

        Assert.Equal(1, OversizedLines(world, first));
        Assert.Equal(1, OversizedLines(world, second));
    }

    [Fact]
    public void An_oversized_value_is_logged_again_on_a_new_level()
    {
        using var world = new World();
        var player = world.Join(1, "A");
        player.FinishLoading();
        string path = RpcProtocol.PathFor(300, 0);

        world.Server.SendRpc(RpcKind.String, path, RpcValue.OfString(new string('Y', 600)), null);
        world.Server.SetLevel("Pack.Level.Other", "Other", 0, null);
        world.Server.SendRpc(RpcKind.String, path, RpcValue.OfString(new string('Y', 600)), null);

        Assert.Equal(2, OversizedLines(world, path));
    }
}
