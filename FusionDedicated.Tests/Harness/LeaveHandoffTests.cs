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
    public void A_prop_left_unowned_is_claimed_by_whoever_grabs_it()
    {
        var (world, leaver, _, rider) = BusyLeaver();
        using var _w = world;

        world.Leave(leaver, "left");
        rider.Send(FusionProtocol.BuildOwnershipRequest(rider.SmallId, FirstCrate));

        Assert.Equal(rider.SmallId, world.Server.Entities.Get(FirstCrate)!.OwnerSmallId);
    }
}
