using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The server as player 0. Fusion looks up the host when an entity has no owner, and
/// no client on this server knew one, so building a received constraint threw and
/// left every later constraint on the builder's screen only.
/// </summary>
public class ServerPlayerTests
{
    private const ulong ServerSteamId = 90071992547409920;

    private static World NewWorld(ServerConfig? config = null)
    {
        var world = new World(config ?? new ServerConfig { CullOrphanedEntities = false, ServerName = "Southside RP" });
        world.Server.HostPlatformId = ServerSteamId;
        return world;
    }

    [Fact]
    public void A_newcomer_is_told_the_server_is_player_0()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");

        var host = joel.View.Players[0];

        Assert.Equal(ServerSteamId, host.PlatformId);
        Assert.Equal("Southside RP", host.Metadata["Username"]);
        Assert.Equal("True", host.Metadata["Loading"]);
        Assert.Equal(ServerPlayer.BlankAvatar, host.AvatarBarcode);
        Assert.False(host.IsInitialJoin);
    }

    [Fact]
    public void Player_0_comes_before_other_players_and_any_catch_up()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);
        joel.Send(ModuleProtocol.WriteModuleToClients(ModuleProtocol.ConstraintCreateTag, joel.SmallId, new byte[16]));

        var kanza = world.Join(76561198000000002, "Kanza");

        var sent = world.Transport.SentTo(kanza.Connection).Select(s => s.Message).ToList();
        int IndexOf(Func<byte[], bool> match) => sent.FindIndex(m => match(m));

        int own = IndexOf(m => FusionProtocol.TryReadConnectionResponse(m) is { } r && r.SmallID == kanza.SmallId);
        int server = IndexOf(m => FusionProtocol.TryReadConnectionResponse(m) is { SmallID: 0 });

        // The world really had catch-up to send, so "straight after" means something.
        Assert.True(IndexOf(m => m[0] == FusionProtocol.TagSpawnResponse) >= 0, "no spawn catch-up was sent");
        Assert.True(IndexOf(m => ModuleProtocol.TryReadHandlerTag(m) == ModuleProtocol.ConstraintCreateTag) >= 0,
            "no constraint catch-up was sent");

        Assert.True(own >= 0, "the newcomer was not told about themselves");
        Assert.Equal(own + 1, server);
    }

    [Fact]
    public void Each_player_is_told_about_player_0_once()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");

        int Told(FakePlayer player) => world.Transport.SentTo(player.Connection)
            .Count(s => FusionProtocol.TryReadConnectionResponse(s.Message) is { SmallID: 0 });

        Assert.Equal(1, Told(joel));
        Assert.Equal(1, Told(kanza));
    }
}
