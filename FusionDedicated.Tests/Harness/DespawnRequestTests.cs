using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

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
}
