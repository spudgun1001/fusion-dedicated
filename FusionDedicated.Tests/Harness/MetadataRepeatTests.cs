using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>A key set to the value it holds is not sent to everybody again, and the server's rank key is never lost to the client's key limit.</summary>
public class MetadataRepeatTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    private static readonly byte[] PathBytes = Convert.FromHexString(RpcProtocol.PathFor(300, 0));

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

    [Fact]
    public void Setting_a_key_to_the_value_it_holds_is_not_sent_again()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ClientMessages.Metadata(joel.SmallId, "mod.state", "ready"));
        joel.Send(ClientMessages.Metadata(joel.SmallId, "mod.state", "ready"));

        Assert.Equal(1, StatesTo(world, kanza, before));
    }

    [Fact]
    public void A_changed_value_is_still_sent()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ClientMessages.Metadata(joel.SmallId, "mod.state", "ready"));
        joel.Send(ClientMessages.Metadata(joel.SmallId, "mod.state", "done"));

        Assert.Equal(2, StatesTo(world, kanza, before));
    }

    [Fact]
    public void Saying_it_has_finished_loading_again_still_sends_the_level_state()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        var kanza = world.Join(KanzaId, "Kanza");
        kanza.Send(GateProtocol.BuildRpcVariable((byte)RpcKind.String, kanza.SmallId, kanza.SmallId,
            RpcProtocol.WriteValue(RpcKind.String, PathBytes, RpcValue.OfString("open"))));

        int before = world.Transport.SentTo(joel.Connection).Count;
        joel.Send(ClientMessages.FinishedLoading(joel.SmallId));

        Assert.Equal(1, world.Transport.SentTo(joel.Connection)
            .Skip(before)
            .Count(sent => sent.Message[0] is >= 209 and <= 214));
    }

    [Fact]
    public void The_rank_lands_when_a_player_joins_with_64_keys_of_their_own()
    {
        using var world = new World();
        var metadata = new Dictionary<string, string> { ["Username"] = "Joel" };

        for (int i = 0; metadata.Count < 64; i++)
        {
            metadata[$"mod.key{i}"] = "x";
        }

        var connection = world.Transport.Connect();
        world.Transport.Deliver(connection, FusionProtocol.BuildConnectionRequest(
            JoelId, new Version(world.Server.Config.VersionMajor, world.Server.Config.VersionMinor, 0),
            "SLZ.BONELAB.Content.Avatar.FordBW", metadata, new List<string>()));
        world.Server.Receive();

        var joel = world.Server.Players.GetByPlatformId(JoelId);

        Assert.NotNull(joel);
        Assert.Equal("DEFAULT", joel!.Metadata.GetValueOrDefault(FusionServer.PermissionMetadataKey));
    }

    [Fact]
    public void A_key_dropped_for_the_limit_is_logged_once_per_player()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, MetadataPerSecond = 0 });
        var joel = world.Join(JoelId, "Joel");

        joel.SendMany(Enumerable.Range(0, 70)
            .Select(i => ClientMessages.Metadata(joel.SmallId, $"mod.key{i}", "x")));

        Assert.Single(world.Server.RecentLog(2000), e => e.Message ==
            "Not keeping all of Joel's metadata: a player may hold 64 keys of up to 256 characters each, " +
            "so players who join later will not get the rest");
    }
}
