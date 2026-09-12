using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
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

    [Theory]
    [InlineData(PermissionLevel.Default)]
    [InlineData(PermissionLevel.Operator)]
    public void Nobody_may_change_player_0s_metadata(PermissionLevel rank)
    {
        var config = new ServerConfig { CullOrphanedEntities = false, ServerName = "Southside RP" };
        config.Permissions.Add(new PermissionEntry { PlatformId = 76561198000000001, Level = rank });
        using var world = NewWorld(config);
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        int Responses() => world.Transport.SentTo(joel.Connection).Count(s =>
            Envelope.Read(s.Message) is { Tag: FusionProtocol.TagPlayerMetadataResponse } envelope
            && envelope.Payload[0] == 0);

        int before = Responses();

        // Loading false would build the server a body on every screen.
        joel.Send(ClientMessages.MetadataFor(joel.SmallId, 0, "Loading", "False"));
        joel.Send(ClientMessages.MetadataFor(joel.SmallId, 0, "Username", "Hijacked"));

        Assert.Equal(before, Responses());
        Assert.Equal("True", joel.View.Players[0].Metadata["Loading"]);
        Assert.Equal("Southside RP", joel.View.Players[0].Metadata["Username"]);
    }

    [Fact]
    public void Kicking_player_0_is_logged_as_aimed_at_the_server()
    {
        var config = new ServerConfig { CullOrphanedEntities = false, ServerName = "Southside RP" };
        config.Permissions.Add(new PermissionEntry { PlatformId = 76561198000000001, Level = PermissionLevel.Operator });
        using var world = NewWorld(config);
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        joel.Send(ClientMessages.PermissionCommand(joel.SmallId, ServerProtocol.PermissionCommand.Kick, 0));

        Assert.Contains(world.Server.RecentLog(), entry => entry.Message.Contains("which is the server"));
        Assert.Contains(joel, world.Players);
    }

    [Fact]
    public void Renaming_the_server_renames_player_0_once()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");

        int Renames() => world.Transport.SentTo(joel.Connection).Count(s =>
            Envelope.Read(s.Message) is { Tag: FusionProtocol.TagPlayerMetadataResponse } envelope
            && envelope.Payload[0] == 0);

        int before = Renames();

        world.Server.Config.ServerName = "Northside RP";
        world.Server.PushSettings();
        world.Server.PushSettings();
        world.Sync();

        Assert.Equal(before + 1, Renames());
        Assert.Equal("Northside RP", joel.View.Players[0].Metadata["Username"]);
    }

    [Fact]
    public void The_first_settings_push_sends_player_0s_name()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");

        int Renames() => world.Transport.SentTo(joel.Connection).Count(s =>
            Envelope.Read(s.Message) is { Tag: FusionProtocol.TagPlayerMetadataResponse } envelope
            && envelope.Payload[0] == 0);

        // The join's settings push is the first, so it sends player 0's name once.
        Assert.Equal(1, Renames());
        Assert.Equal("Southside RP", joel.View.Players[0].Metadata["Username"]);
    }
}
