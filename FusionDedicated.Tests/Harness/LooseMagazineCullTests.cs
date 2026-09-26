using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Pouch magazines were never culled while their owner was online (113 Glock mags in
/// one session), and a round loaded into a gun kept its attached flag for good.
/// </summary>
public class LooseMagazineCullTests
{
    private const string Mag = "Rexmeck.WeaponPackLT.Spawnable.Magglock17gen5";
    private const string Gun = "Rexmeck.WeaponPackLT.Spawnable.GLOCK17";

    private static (World World, FakePlayer Kanza) Build()
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = true });
        var kanza = world.Join(76561198000000001, "Kanza");
        kanza.FinishLoading();
        return (world, kanza);
    }

    private static void Idle(World world)
    {
        world.Advance(TimeSpan.FromSeconds(61));
        world.Tick();
    }

    [Fact]
    public void A_loose_pouch_magazine_goes_after_a_minute_while_its_owner_is_online()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Mag, 0, 0, 0);
        Idle(world);

        Assert.Null(world.Server.Entities.Get(300));
    }

    [Fact]
    public void A_magazine_in_a_gun_on_the_floor_stays()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Mag, 0, 0, 0);
        world.Spawn(kanza, 301, Gun, 0, 0, 0);
        kanza.Send(ClientMessages.MagazineInsert(kanza.SmallId, 300, 301));
        Idle(world);

        Assert.NotNull(world.Server.Entities.Get(300));
    }

    [Fact]
    public void A_magazine_in_a_body_slot_stays()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Mag, 0, 0, 0);
        kanza.Send(ClientMessages.SlotInsert(kanza.SmallId, kanza.SmallId, 300, 2));
        Idle(world);

        Assert.NotNull(world.Server.Entities.Get(300));
    }

    [Fact]
    public void A_held_magazine_stays()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Mag, 0, 0, 0);
        kanza.Grab(300);
        Idle(world);

        Assert.NotNull(world.Server.Entities.Get(300));
    }

    [Fact]
    public void A_round_replaced_by_the_next_one_in_the_same_gun_goes()
    {
        // Shells and rounds are cleared on insert, so no eject ever arrives for them.
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Mag, 0, 0, 0);
        world.Spawn(kanza, 301, Mag, 0, 0, 0);
        world.Spawn(kanza, 302, Gun, 0, 0, 0);
        kanza.Send(ClientMessages.MagazineInsert(kanza.SmallId, 300, 302));
        kanza.Send(ClientMessages.MagazineInsert(kanza.SmallId, 301, 302));
        Idle(world);

        Assert.Null(world.Server.Entities.Get(300));
        Assert.NotNull(world.Server.Entities.Get(301));
    }

    [Fact]
    public void A_magazine_whose_gun_is_gone_goes()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Mag, 0, 0, 0);
        world.Spawn(kanza, 301, Gun, 0, 0, 0);
        kanza.Send(ClientMessages.MagazineInsert(kanza.SmallId, 300, 301));
        kanza.Send(ClientMessages.Despawn(kanza.SmallId, 301));
        Idle(world);

        Assert.Null(world.Server.Entities.Get(301));
        Assert.Null(world.Server.Entities.Get(300));
    }
}
