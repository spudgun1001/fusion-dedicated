using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Metadata, avatar swaps and RPC messages over a player's allowance are dropped before they are handled or passed on.</summary>
public class FloodLimitTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    private static readonly byte[] PathBytes = Convert.FromHexString(RpcProtocol.PathFor(300, 0));

    private static ServerConfig Limits(int metadata = 3, int avatars = 2, int rpc = 3) => new()
    {
        CullOrphanedEntities = false,
        MetadataPerSecond = metadata,
        AvatarSwapsPerSecond = avatars,
        RpcMessagesPerSecond = rpc,
    };

    /// <summary>Two loaded players, a second after their loading messages were counted.</summary>
    private static (FakePlayer Joel, FakePlayer Kanza) Loaded(World world)
    {
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(1));

        return (joel, kanza);
    }

    private static IEnumerable<byte[]> States(FakePlayer player, int count, string prefix = "v")
        => Enumerable.Range(0, count)
            .Select(i => ClientMessages.Metadata(player.SmallId, "mod.state", $"{prefix}{i}"));

    private static int StatesTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(sent => Envelope.Read(sent.Message) is { Tag: GateProtocol.TagPlayerMetadataResponse } envelope
                           && KeyOf(envelope.Payload) == "mod.state");

    private static string? KeyOf(byte[] payload)
    {
        var reader = new FusionNetReader(payload);
        reader.ReadByte();

        return reader.ReadString();
    }

    /// <summary>A PlayerRepAvatar message: 420 bytes of proportions, then the barcode.</summary>
    private static byte[] Avatar(FakePlayer player, string barcode)
    {
        var payload = new FusionNetWriter(512);
        payload.WriteRaw(ClientMessages.AvatarStats());
        payload.Write(barcode);

        var message = new FusionNetWriter(600);
        message.Write(GateProtocol.TagPlayerRepAvatar);
        message.Write((byte)3);     // ToOtherClients
        message.Write((byte)0);     // Reliable
        message.WriteNullable(player.SmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    private static int AvatarsTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(sent => sent.Message[0] == GateProtocol.TagPlayerRepAvatar);

    private static byte[] Rpc(FakePlayer from, FakePlayer to, string value)
        => GateProtocol.BuildRpcVariable((byte)RpcKind.String, to.SmallId, from.SmallId,
            RpcProtocol.WriteValue(RpcKind.String, PathBytes, RpcValue.OfString(value)));

    private static int RpcTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(sent => sent.Message[0] is >= 209 and <= 214);

    [Fact]
    public void Metadata_within_the_allowance_all_reaches_the_other_player()
    {
        using var world = new World(Limits(metadata: 3));
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(States(joel, 3));

        Assert.Equal(3, StatesTo(world, kanza, before));
    }

    [Fact]
    public void Metadata_over_the_allowance_is_dropped_before_it_is_kept_or_passed_on()
    {
        using var world = new World(Limits(metadata: 3));
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(States(joel, 5));

        Assert.Equal(3, StatesTo(world, kanza, before));
        Assert.Equal("v2", world.Server.Players.GetByPlatformId(JoelId)!.Metadata["mod.state"]);
    }

    [Fact]
    public void An_unchanged_repeat_does_not_spend_the_allowance_a_real_change_needs()
    {
        using var world = new World(Limits(metadata: 2));
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(new[]
        {
            ClientMessages.Metadata(joel.SmallId, "mod.state", "ready"),
            ClientMessages.Metadata(joel.SmallId, "mod.state", "ready"),
            ClientMessages.Metadata(joel.SmallId, "mod.state", "ready"),
            ClientMessages.Metadata(joel.SmallId, "mod.other", "changed"),
        });

        Assert.Contains(world.Transport.SentTo(kanza.Connection).Skip(before),
            sent => Envelope.Read(sent.Message) is { Tag: GateProtocol.TagPlayerMetadataResponse } envelope
                    && KeyOf(envelope.Payload) == "mod.other");
    }

    [Fact]
    public void The_metadata_allowance_resets_each_second()
    {
        using var world = new World(Limits(metadata: 3));
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(States(joel, 4));
        world.Advance(TimeSpan.FromSeconds(1));
        joel.SendMany(States(joel, 1, prefix: "w"));

        Assert.Equal(4, StatesTo(world, kanza, before));
    }

    [Fact]
    public void Avatar_swaps_over_the_allowance_are_dropped_and_not_passed_on()
    {
        using var world = new World(Limits(avatars: 2));
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(Enumerable.Range(0, 3).Select(i => Avatar(joel, $"SLZ.BONELAB.Content.Avatar.Swap{i}")));

        Assert.Equal(2, AvatarsTo(world, kanza, before));
        Assert.Equal("SLZ.BONELAB.Content.Avatar.Swap1", world.Server.Players.GetByPlatformId(JoelId)!.AvatarBarcode);
    }

    [Fact]
    public void Rpc_messages_over_the_allowance_are_dropped_and_not_passed_on()
    {
        using var world = new World(Limits(rpc: 3));
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(Enumerable.Range(0, 4).Select(i => Rpc(joel, kanza, $"v{i}")));

        Assert.Equal(3, RpcTo(world, kanza, before));
    }

    [Fact]
    public void An_exempt_player_is_not_limited()
    {
        var config = Limits(metadata: 3);
        config.Permissions.Add(new PermissionEntry { PlatformId = JoelId, Username = "Joel", Level = PermissionLevel.Owner });

        using var world = new World(config);
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(States(joel, 5));

        Assert.Equal(5, StatesTo(world, kanza, before));
    }

    [Fact]
    public void Nobody_is_limited_while_anti_spam_is_off()
    {
        var config = Limits(metadata: 3);
        config.AntiSpamEnabled = false;

        using var world = new World(config);
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(States(joel, 5));

        Assert.Equal(5, StatesTo(world, kanza, before));
    }

    [Fact]
    public void Dropped_messages_are_summed_up_once_a_minute()
    {
        using var world = new World(Limits(metadata: 3));
        var (joel, _) = Loaded(world);

        joel.SendMany(States(joel, 5));

        world.Advance(TimeSpan.FromSeconds(30));
        world.Tick();

        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.StartsWith("Dropped "));

        world.Advance(TimeSpan.FromSeconds(31));
        world.Tick();

        Assert.Single(world.Server.RecentLog(2000),
            e => e.Message == "Dropped 2 metadata messages from Joel in the last minute");
    }

    [Fact]
    public void A_player_who_leaves_takes_their_allowance_with_them()
    {
        using var world = new World(Limits(metadata: 2));
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");

        joel.SendMany(States(joel, 2));
        byte smallId = joel.SmallId;
        world.Leave(joel, "Closing Connection");

        var newbie = world.Join(76561198000000003, "Newbie");
        Assert.Equal(smallId, newbie.SmallId);

        int before = world.Transport.SentTo(kanza.Connection).Count;
        newbie.SendMany(States(newbie, 2, prefix: "n"));

        Assert.Equal(2, StatesTo(world, kanza, before));
    }

    [Fact]
    public void Finishing_loading_still_gets_the_level_state_when_the_allowance_is_spent()
    {
        using var world = new World(Limits(metadata: 1));
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.Send(Rpc(kanza, kanza, "open"));

        joel.Send(ClientMessages.Metadata(joel.SmallId, "mod.state", "v0"));
        int before = world.Transport.SentTo(joel.Connection).Count;

        joel.Send(ClientMessages.FinishedLoading(joel.SmallId));

        Assert.Equal(1, RpcTo(world, joel, before));
    }

    [Fact]
    public void Finishing_loading_does_not_use_up_the_allowance()
    {
        using var world = new World(Limits(metadata: 1));
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ClientMessages.FinishedLoading(joel.SmallId));
        joel.Send(ClientMessages.Metadata(joel.SmallId, "mod.state", "v0"));

        Assert.Equal(1, StatesTo(world, kanza, before));
    }
}
