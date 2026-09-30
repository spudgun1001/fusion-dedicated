using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A leaver's ~590 props all went to one heir, whose game then timed out
/// taking them on. Only what somebody holds or rides is handed on now.
/// </summary>
public class LeaveHandoffTests
{
    private const ushort Van = 500;
    private const ushort Gun = 501;
    private const ushort FirstCrate = 1000;
    private const int Crates = 600;

    private static (World World, FakePlayer Leaver, FakePlayer Holder, FakePlayer Rider) BusyLeaver()
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = false, OwnershipRequestsPerSecond = 0 });
        var leaver = world.Join(76561198000000001, "Leaver");
        var holder = world.Join(76561198000000002, "Holder");
        var rider = world.Join(76561198000000003, "Rider");

        foreach (var player in world.Players)
        {
            player.FinishLoading();
        }

        world.Spawn(leaver, Van, "BaBaCorp.AssortedAutomobiles.Spawnable.VanSWATTransport", 0, 0, 0);
        world.Spawn(leaver, Gun, "spudgun1001.Guns.Spawnable.Eder22", 0, 0, 0);

        for (var i = 0; i < Crates; i++)
        {
            world.Server.Entities.Register((ushort)(FirstCrate + i), "Pack.Spawnable.Crate", leaver.SmallId, 0, 0, 0);
        }

        rider.Send(FusionProtocol.BuildSeat(rider.SmallId, Van, 1, true));
        holder.Send(FusionProtocol.BuildGrab(holder.SmallId, FusionProtocol.Handedness.RIGHT, 0, Gun));

        return (world, leaver, holder, rider);
    }

    [Fact]
    public void Unheld_props_are_left_unowned_rather_than_piled_on_one_player()
    {
        var (world, leaver, _, _) = BusyLeaver();
        using var _w = world;

        world.Leave(leaver, "left");

        for (var i = 0; i < Crates; i++)
        {
            Assert.Null(world.Server.Entities.Get((ushort)(FirstCrate + i))!.OwnerSmallId);
        }
    }

    [Fact]
    public void Held_and_ridden_things_still_go_to_the_holder_and_the_rider()
    {
        var (world, leaver, holder, rider) = BusyLeaver();
        using var _w = world;

        world.Leave(leaver, "left");

        Assert.Equal(holder.SmallId, world.Server.Entities.Get(Gun)!.OwnerSmallId);
        Assert.Equal(rider.SmallId, world.Server.Entities.Get(Van)!.OwnerSmallId);
    }

    [Fact]
    public void A_leavers_held_and_holstered_guns_and_their_magazines_leave_with_them()
    {
        const ushort Held = 502, Holstered = 503, Magazine = 504, HeldMagazine = 510;
        var (world, leaver, holder, rider) = BusyLeaver();
        using var _w = world;
        world.Spawn(leaver, Held, "Pack.Spawnable.Rifle", 0, 0, 0);
        world.Spawn(leaver, HeldMagazine, "Pack.Spawnable.MagazineRifle", 0, 0, 0);
        world.Spawn(leaver, Holstered, "Pack.Spawnable.Pistol", 0, 0, 0);
        world.Spawn(leaver, Magazine, "Pack.Spawnable.MagazinePistol", 0, 0, 0);

        // A magazine in it is what shows the held rifle is a gun.
        leaver.Send(ClientMessages.MagazineInsert(leaver.SmallId, HeldMagazine, Held));
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, Held));
        leaver.Send(ClientMessages.SlotInsert(leaver.SmallId, leaver.SmallId, Holstered, 1));
        leaver.Send(ClientMessages.MagazineInsert(leaver.SmallId, Magazine, Holstered));

        world.Leave(leaver, "left");

        foreach (ushort id in new[] { Held, HeldMagazine, Holstered, Magazine })
        {
            Assert.Null(world.Server.Entities.Get(id));
            Assert.False(holder.View.Entities.ContainsKey(id), $"entity {id} is still on the holder's screen");
            Assert.False(rider.View.Entities.ContainsKey(id), $"entity {id} is still on the rider's screen");
        }

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.NotNull(world.Server.Entities.Get(FirstCrate));
    }

    private const string Pistol = "Pack.Spawnable.Pistol";

    /// <summary>The leaver holds a second pistol of that model with nothing in it, so only the earlier holster shows it is a gun.</summary>
    private static void AssertHeldPistolLeavesWith(World world, FakePlayer leaver, FakePlayer holder, FakePlayer rider)
    {
        const ushort Held = 513;
        world.Spawn(leaver, Held, Pistol, 0, 0, 0);
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, Held));

        world.Leave(leaver, "left");

        Assert.Null(world.Server.Entities.Get(Held));
        Assert.False(holder.View.Entities.ContainsKey(Held), "the held pistol is still on the holder's screen");
        Assert.False(rider.View.Entities.ContainsKey(Held), "the held pistol is still on the rider's screen");
    }

    [Fact]
    public void A_held_gun_whose_model_has_been_holstered_leaves_with_them()
    {
        var (world, leaver, holder, rider) = BusyLeaver();
        using var _w = world;
        world.Spawn(holder, 512, Pistol, 0, 0, 0);
        holder.Send(ClientMessages.SlotInsert(holder.SmallId, holder.SmallId, 512, 1));

        AssertHeldPistolLeavesWith(world, leaver, holder, rider);
    }

    [Fact]
    public void A_held_gun_whose_model_a_plugin_holstered_leaves_with_them()
    {
        var (world, leaver, holder, rider) = BusyLeaver();
        using var _w = world;
        ushort bought = world.Server.SpawnForPlayer(Pistol, 0f, 1f, 0f, Array.Empty<byte>(), 76561198000000002);
        world.Sync();
        Assert.True(world.Server.HolsterForPlugin(bought, 76561198000000002, 1));
        world.Sync();

        AssertHeldPistolLeavesWith(world, leaver, holder, rider);
    }

    [Fact]
    public void Something_the_leaver_held_but_did_not_own_stays()
    {
        const ushort HoldersRifle = 505, RifleMagazine = 511;
        var (world, leaver, holder, _) = BusyLeaver();
        using var _w = world;
        world.Spawn(holder, HoldersRifle, "Pack.Spawnable.Rifle", 0, 0, 0);
        world.Spawn(holder, RifleMagazine, "Pack.Spawnable.MagazineRifle", 0, 0, 0);
        holder.Send(ClientMessages.MagazineInsert(holder.SmallId, RifleMagazine, HoldersRifle));
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, HoldersRifle));

        world.Leave(leaver, "left");

        Assert.Equal(holder.SmallId, world.Server.Entities.Get(HoldersRifle)!.OwnerSmallId);
        Assert.True(holder.View.Entities.ContainsKey(HoldersRifle));
    }

    [Fact]
    public void A_vehicle_the_leaver_drove_alone_is_not_despawned()
    {
        const ushort Car = 506;
        var (world, leaver, holder, _) = BusyLeaver();
        using var _w = world;
        world.Spawn(leaver, Car, "BaBaCorp.AssortedAutomobiles.Spawnable.Sedan", 0, 0, 0);
        leaver.Send(FusionProtocol.BuildSeat(leaver.SmallId, Car, 0, true));
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, Car));

        world.Leave(leaver, "left");

        Assert.Null(world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.True(holder.View.Entities.ContainsKey(Car));
    }

    [Fact]
    public void A_crate_the_leaver_held_is_left_unowned_not_despawned()
    {
        var (world, leaver, holder, _) = BusyLeaver();
        using var _w = world;
        world.Spawn(leaver, 507, "Pack.Spawnable.Crate", 0, 0, 0);
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, 507));

        world.Leave(leaver, "left");

        Assert.Null(world.Server.Entities.Get(507)!.OwnerSmallId);
        Assert.True(holder.View.Entities.ContainsKey(507));
    }

    [Fact]
    public void Something_anybody_sat_in_is_not_despawned_even_with_a_magazine_in_it()
    {
        const ushort Turret = 508, Belt = 509;
        var (world, leaver, holder, _) = BusyLeaver();
        using var _w = world;
        world.Spawn(leaver, Turret, "Pack.Spawnable.SeatedTurret", 0, 0, 0);
        world.Spawn(leaver, Belt, "Pack.Spawnable.TurretBelt", 0, 0, 0);
        leaver.Send(ClientMessages.MagazineInsert(leaver.SmallId, Belt, Turret));
        leaver.Send(FusionProtocol.BuildSeat(leaver.SmallId, Turret, 0, true));
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, Turret));

        world.Leave(leaver, "left");

        Assert.NotNull(world.Server.Entities.Get(Turret));
        Assert.True(holder.View.Entities.ContainsKey(Turret));
    }

    [Fact]
    public void A_prop_left_unowned_is_claimed_by_whoever_grabs_it()
    {
        var (world, leaver, _, rider) = BusyLeaver();
        using var _w = world;

        world.Leave(leaver, "left");
        rider.Send(FusionProtocol.BuildOwnershipRequest(rider.SmallId, FirstCrate));

        Assert.Equal(rider.SmallId, world.Server.Entities.Get(FirstCrate)!.OwnerSmallId);
    }
}
