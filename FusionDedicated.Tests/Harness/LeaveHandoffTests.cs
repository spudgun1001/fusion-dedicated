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
        const ushort Held = 502, Holstered = 503, Magazine = 504;
        var (world, leaver, holder, rider) = BusyLeaver();
        using var _w = world;
        world.Spawn(leaver, Held, "Pack.Spawnable.Pistol", 0, 0, 0);
        world.Spawn(leaver, Holstered, "Pack.Spawnable.Pistol", 0, 0, 0);
        world.Spawn(leaver, Magazine, "Pack.Spawnable.MagazinePistol", 0, 0, 0);
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, Held));
        leaver.Send(ClientMessages.SlotInsert(leaver.SmallId, leaver.SmallId, Holstered, 1));
        leaver.Send(ClientMessages.MagazineInsert(leaver.SmallId, Magazine, Holstered));

        world.Leave(leaver, "left");

        foreach (ushort id in new[] { Held, Holstered, Magazine })
        {
            Assert.Null(world.Server.Entities.Get(id));
            Assert.False(holder.View.Entities.ContainsKey(id), $"entity {id} is still on the holder's screen");
            Assert.False(rider.View.Entities.ContainsKey(id), $"entity {id} is still on the rider's screen");
        }

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.NotNull(world.Server.Entities.Get(FirstCrate));
    }

    [Fact]
    public void Something_the_leaver_held_but_did_not_own_stays()
    {
        const ushort HoldersRifle = 505;
        var (world, leaver, holder, _) = BusyLeaver();
        using var _w = world;
        world.Spawn(holder, HoldersRifle, "Pack.Spawnable.Rifle", 0, 0, 0);
        leaver.Send(FusionProtocol.BuildGrab(leaver.SmallId, FusionProtocol.Handedness.LEFT, 0, HoldersRifle));

        world.Leave(leaver, "left");

        Assert.Equal(holder.SmallId, world.Server.Entities.Get(HoldersRifle)!.OwnerSmallId);
        Assert.True(holder.View.Entities.ContainsKey(HoldersRifle));
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
