using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Tests.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A tool resent one despawn 17 times in a second and each copy went to everybody again. A kept
/// prop despawned by a player stayed gone for the people there and came back for later joiners.
/// </summary>
public class DespawnRequestTests
{
    private const ulong JoelId = 76561198000000001;
    private const ushort Crate = 300;

    private static bool IsDespawnOf(byte[] message, ushort entity)
    {
        if (Envelope.Read(message) is not { Tag: ServerProtocol.TagDespawnResponse } envelope)
        {
            return false;
        }

        var reader = new FusionNetReader(envelope.Payload);
        reader.ReadByte(); // despawner

        return reader.ReadUInt16() == entity;
    }

    private static int DespawnsTo(World world, FakePlayer player, ushort entity, int from = 0)
        => world.Transport.SentTo(player.Connection).Skip(from).Count(sent => IsDespawnOf(sent.Message, entity));

    private static int DataRequestsTo(World world, FakePlayer player, FakePlayer from, ushort entity, int skip = 0)
        => world.Transport.SentTo(player.Connection).Skip(skip).Count(sent =>
            Envelope.Read(sent.Message) is { Tag: FusionProtocol.TagEntityDataRequest } envelope
            && envelope.Sender == from.SmallId
            && FusionProtocol.TryReadEntityDataRequest(sent.Message)?.EntityId == entity);

    private static int DespawnLines(World world, ushort entity)
        => world.Server.RecentLog(2000).Count(e => e.Message.StartsWith($"Despawn: id={entity} "));

    private static (World World, FakePlayer Joel, FakePlayer Kanza) CrateOwnedByJoel(ServerConfig? config = null)
    {
        var world = new World(config);
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);

        return (world, joel, kanza);
    }

    [Fact]
    public void A_despawn_sent_again_within_five_seconds_goes_out_once()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        for (var i = 0; i < 17; i++)
        {
            joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));
        }

        world.Advance(TimeSpan.FromMilliseconds(4999));
        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));

        Assert.Null(world.Server.Entities.Get(Crate));
        Assert.Equal(1, DespawnsTo(world, kanza, Crate));
        Assert.Equal(1, DespawnLines(world, Crate));
    }

    [Fact]
    public void A_despawn_of_a_removed_id_after_five_seconds_still_goes_out()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));
        world.Advance(TimeSpan.FromSeconds(5));
        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));

        Assert.Equal(2, DespawnsTo(world, kanza, Crate));
    }

    [Fact]
    public void A_despawn_of_an_id_the_server_never_tracked_still_goes_out()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        joel.Send(ClientMessages.Despawn(joel.SmallId, 999));

        Assert.Equal(1, DespawnsTo(world, kanza, 999));
        Assert.Equal(1, DespawnLines(world, 999));
    }

    [Fact]
    public void A_new_prop_given_a_removed_id_can_be_despawned_straight_away()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));
        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 4, 5, 6);
        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));

        Assert.Null(world.Server.Entities.Get(Crate));
        Assert.Equal(2, DespawnsTo(world, kanza, Crate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_owner_rank_player_despawning_a_kept_prop_is_refused(bool extendedProtection)
    {
        var config = new ServerConfig { CullOrphanedEntities = false, ExtendedProtection = extendedProtection };
        config.Permissions.Add(new PermissionEntry { PlatformId = JoelId, Username = "Joel", Level = PermissionLevel.Owner });

        var (world, joel, kanza) = CrateOwnedByJoel(config);
        using var _ = world;
        world.Server.Entities.Get(Crate)!.Persistent = true;
        int joelBefore = world.Transport.SentTo(joel.Connection).Count;
        int kanzaBefore = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));

        Assert.NotNull(world.Server.Entities.Get(Crate));
        Assert.Equal(0, DespawnsTo(world, joel, Crate, joelBefore));
        Assert.Equal(0, DespawnsTo(world, kanza, Crate, kanzaBefore));
        Assert.Equal(0, DespawnLines(world, Crate));

        var warning = Assert.Single(world.Server.RecentLog(2000), e => e.Level == "WARN" && e.Message.Contains($"kept prop {Crate}"));
        Assert.Equal("Joel tried to despawn kept prop 300 ('Crate'); remove it from the control panel", warning.Message);
    }

    /// <summary>Kanza's game finishes downloading the crate's mod after Joel despawned it, then asks for its state.</summary>
    private static (int Joel, int Kanza) LateSpawnAsks(World world, FakePlayer joel, FakePlayer kanza, ushort entity)
    {
        int joelBefore = world.Transport.SentTo(joel.Connection).Count;
        int kanzaBefore = world.Transport.SentTo(kanza.Connection).Count;

        kanza.Send(FusionProtocol.BuildEntityDataRequest(kanza.SmallId, joel.SmallId, entity));

        return (joelBefore, kanzaBefore);
    }

    [Fact]
    public void A_late_spawn_of_a_despawned_crate_is_despawned_for_that_player_alone()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));
        world.Advance(TimeSpan.FromMinutes(2));

        var (joelBefore, kanzaBefore) = LateSpawnAsks(world, joel, kanza, Crate);

        Assert.Equal(1, DespawnsTo(world, kanza, Crate, kanzaBefore));
        Assert.Equal(0, DespawnsTo(world, joel, Crate, joelBefore));
        Assert.Equal(0, DataRequestsTo(world, joel, kanza, Crate, joelBefore));
    }

    [Fact]
    public void A_data_request_for_a_live_crate_is_still_passed_on()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        var (joelBefore, kanzaBefore) = LateSpawnAsks(world, joel, kanza, Crate);

        Assert.Equal(1, DataRequestsTo(world, joel, kanza, Crate, joelBefore));
        Assert.Equal(0, DespawnsTo(world, kanza, Crate, kanzaBefore));
    }

    [Fact]
    public void A_data_request_for_an_id_the_server_never_tracked_is_still_passed_on()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        var (joelBefore, kanzaBefore) = LateSpawnAsks(world, joel, kanza, 999);

        Assert.Equal(1, DataRequestsTo(world, joel, kanza, 999, joelBefore));
        Assert.Equal(0, DespawnsTo(world, kanza, 999, kanzaBefore));
    }

    [Fact]
    public void A_data_request_for_a_crate_spawned_again_under_its_old_id_is_still_passed_on()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));
        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 4, 5, 6);
        world.Advance(TimeSpan.FromMinutes(2));

        var (joelBefore, kanzaBefore) = LateSpawnAsks(world, joel, kanza, Crate);

        Assert.Equal(1, DataRequestsTo(world, joel, kanza, Crate, joelBefore));
        Assert.Equal(0, DespawnsTo(world, kanza, Crate, kanzaBefore));
    }

    [Fact]
    public void A_data_request_for_a_crate_despawned_thirty_minutes_ago_is_still_passed_on()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        joel.Send(ClientMessages.Despawn(joel.SmallId, Crate));
        world.Advance(TimeSpan.FromMinutes(30));

        var (joelBefore, kanzaBefore) = LateSpawnAsks(world, joel, kanza, Crate);

        Assert.Equal(1, DataRequestsTo(world, joel, kanza, Crate, joelBefore));
        Assert.Equal(0, DespawnsTo(world, kanza, Crate, kanzaBefore));
    }

    /// <summary>Joel grabs a piece of the level, which the server learns of from a pose or an unqueue request.</summary>
    private static ushort DiscoverSceneProp(World world, FakePlayer joel, bool byUnqueue)
    {
        if (!byUnqueue)
        {
            joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, 355, new Vec3(9, 9, 9), default, default, default));
            return 355;
        }

        var data = new OracleWriter();
        data.Write(joel.SmallId);
        data.Write((ushort)4242);

        var message = new OracleWriter();
        message.Write(FusionProtocol.TagEntityUnqueueRequest);
        message.Write((byte)1);         // ToServer
        message.Write((byte)0);         // Reliable
        message.Write((byte?)joel.SmallId);
        message.Write(data.ToArray());

        joel.Send(message.ToArray());

        return world.Server.Entities.Entities.Single(e => e.Discovered).Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_data_request_for_a_removed_scene_prop_is_still_passed_on(bool byUnqueue)
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        ushort door = DiscoverSceneProp(world, joel, byUnqueue);
        Assert.Equal("", world.Server.Entities.Get(door)!.Barcode);

        joel.Send(ClientMessages.Despawn(joel.SmallId, door));
        world.Advance(TimeSpan.FromMinutes(2));

        var (joelBefore, kanzaBefore) = LateSpawnAsks(world, joel, kanza, door);

        Assert.Equal(0, DespawnsTo(world, kanza, door, kanzaBefore));
        Assert.Equal(1, DataRequestsTo(world, joel, kanza, door, joelBefore));
    }

    [Fact]
    public void A_crate_spawned_under_a_removed_scene_props_id_is_despawned_for_a_late_spawner()
    {
        var (world, joel, kanza) = CrateOwnedByJoel();
        using var _ = world;

        ushort door = DiscoverSceneProp(world, joel, byUnqueue: false);
        joel.Send(ClientMessages.Despawn(joel.SmallId, door));
        world.Spawn(joel, door, "Pack.Spawnable.Crate", 4, 5, 6);
        joel.Send(ClientMessages.Despawn(joel.SmallId, door));
        world.Advance(TimeSpan.FromMinutes(2));

        var (_, kanzaBefore) = LateSpawnAsks(world, joel, kanza, door);

        Assert.Equal(1, DespawnsTo(world, kanza, door, kanzaBefore));
    }
}
