using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

public class HolsterForPluginTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    [Fact]
    public void A_plugin_holsters_an_item_into_the_players_own_slot_for_everyone()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);
        world.Sync();

        Assert.True(world.Server.HolsterForPlugin(id, KanzaId, 3));
        world.Sync();

        Assert.Equal(id, kanza.View.Slots[((ushort)kanza.SmallId, (byte)3)]);
        Assert.Equal(id, joel.View.Slots[((ushort)kanza.SmallId, (byte)3)]);
    }

    [Fact]
    public void A_player_joining_later_sees_the_plugin_holstered_item()
    {
        using var world = new World();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);
        world.Sync();
        world.Server.HolsterForPlugin(id, KanzaId, 1);

        var late = world.Join(JoelId, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(id, late.View.Slots[((ushort)kanza.SmallId, (byte)1)]);
    }

    [Fact]
    public void A_player_who_builds_a_plugin_holstered_gun_late_still_sees_it_in_the_buyers_slot()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();
        joel.View.BuildsSpawnsLate = true;

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);
        world.Sync();
        world.Server.HolsterForPlugin(id, KanzaId, 3);
        world.Sync();
        world.Advance(TimeSpan.FromSeconds(10));

        joel.BuildSpawns();

        Assert.Equal(id, joel.View.Slots[((ushort)kanza.SmallId, (byte)3)]);
    }

    [Fact]
    public void A_buyer_who_builds_the_gun_late_gets_it_holstered()
    {
        using var world = new World();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();
        kanza.View.BuildsSpawnsLate = true;

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);
        world.Sync();
        world.Server.HolsterForPlugin(id, KanzaId, 3);
        world.Sync();
        world.Advance(TimeSpan.FromSeconds(10));

        kanza.BuildSpawns();
        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, id, new Vec3(0, 0.5f, 0), default, default, default));
        world.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(id, kanza.View.Slots[((ushort)kanza.SmallId, (byte)3)]);
    }

    private static int HolstersSentTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection)
            .Count(sent => ModuleProtocol.TryReadHandlerTag(sent.Message) == ModuleProtocol.InventorySlotInsertTag);

    /// <summary>The buyer builds the gun a second in, so the +2 s holster reaches them before any pose.</summary>
    private static (World World, FakePlayer Kanza, ushort Id) BoughtAndHolstered()
    {
        var world = new World();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();
        kanza.View.BuildsSpawnsLate = true;

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);
        world.Sync();
        world.Server.HolsterForPlugin(id, KanzaId, 3);
        world.Advance(TimeSpan.FromSeconds(1));
        kanza.BuildSpawns();
        world.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(id, kanza.View.Slots[((ushort)kanza.SmallId, (byte)3)]);
        return (world, kanza, id);
    }

    private static void Draw(FakePlayer kanza) => kanza.SendMany(new[]
    {
        ClientMessages.SlotDrop(kanza.SmallId, kanza.SmallId, kanza.SmallId, 3, (byte)FusionProtocol.Handedness.RIGHT),
        FusionProtocol.BuildGrab(kanza.SmallId, FusionProtocol.Handedness.RIGHT, 0, BoughtId(kanza)),
    });

    private static ushort BoughtId(FakePlayer kanza) => kanza.View.Entities.Keys.Single();

    [Fact]
    public void A_gun_drawn_before_the_buyers_first_pose_is_never_put_back()
    {
        var (world, kanza, id) = BoughtAndHolstered();
        using var _ = world;

        Draw(kanza);
        kanza.Send(ClientMessages.SlotInsert(kanza.SmallId, kanza.SmallId, id, 5));
        int before = HolstersSentTo(world, kanza);

        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, id, new Vec3(0, 0.5f, 0), default, default, default));
        world.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(before, HolstersSentTo(world, kanza));
    }

    [Fact]
    public void A_draw_that_arrives_just_after_the_buyers_first_pose_is_not_undone()
    {
        var (world, kanza, id) = BoughtAndHolstered();
        using var _ = world;
        int before = HolstersSentTo(world, kanza);

        // The pose is unreliable and can overtake the reliable drop and grab it followed.
        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, id, new Vec3(0, 0.5f, 0), default, default, default));
        Draw(kanza);
        world.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(before, HolstersSentTo(world, kanza));
    }

    [Fact]
    public void Holstering_for_a_missing_player_or_entity_is_refused()
    {
        using var world = new World();
        world.Join(KanzaId, "Kanza").FinishLoading();

        ushort id = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), KanzaId);

        Assert.False(world.Server.HolsterForPlugin(id, 76561198000000099, 1));
        Assert.False(world.Server.HolsterForPlugin(9999, KanzaId, 1));
    }
}
