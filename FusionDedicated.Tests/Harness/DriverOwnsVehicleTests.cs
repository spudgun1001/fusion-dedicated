using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Only the driver's seat takes a vehicle. In a transport van every passenger's
/// pose took it off the driver, the driver's next pose took it back, and in
/// between everybody else's poses were thrown away as a non-owner's.
/// </summary>
public class DriverOwnsVehicleTests
{
    private const ushort Van = 500;

    private static (World World, FakePlayer Driver, FakePlayer[] Passengers) VanWithThree(bool driverLock)
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = false, OwnershipRequestsPerSecond = 0 });
        var driver = world.Join(76561198000000001, "s1mple");
        var front = world.Join(76561198000000002, "PokeMrowa");
        var back = world.Join(76561198000000003, "FOLZY");

        foreach (var player in world.Players)
        {
            player.FinishLoading();

            if (driverLock)
            {
                player.View.DriverLockedVehicles.Add(Van);
            }
        }

        world.Spawn(driver, Van, "BaBaCorp.AssortedAutomobiles.Spawnable.VanSWATTransport", 0, 0, 0);

        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Van, 0, true));
        front.Send(FusionProtocol.BuildSeat(front.SmallId, Van, 1, true));
        back.Send(FusionProtocol.BuildSeat(back.SmallId, Van, 2, true));

        return (world, driver, new[] { front, back });
    }

    private static void Pose(FakePlayer player, float x)
        => player.Send(FusionProtocol.BuildEntityPoseUpdate(player.SmallId, Van, new Vec3(x, 0, 0),
            default, default, default));

    /// <summary>Owner announcements naming somebody other than the driver, as each client received them.</summary>
    private static int AnnouncementsNaming(World world, FakePlayer player, byte owner, int from)
        => world.Transport.SentTo(player.Connection)
            .Skip(from)
            .Count(sent => FusionProtocol.TryReadOwnershipResponse(sent.Message) is { } response
                && response.EntityId == Van && response.PlayerId == owner);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_passengers_pose_does_not_take_the_van_from_the_driver(bool driverLock)
    {
        var (world, driver, passengers) = VanWithThree(driverLock);
        using var _ = world;
        int[] before = world.Players.Select(p => world.Transport.SentTo(p.Connection).Count).ToArray();

        // Every occupant's game sends the van's pose. The ownership hold expires
        // between rounds, so each one is judged on its own, as it is in a real ride.
        for (var tick = 1; tick <= 10; tick++)
        {
            Pose(driver, tick);

            foreach (var passenger in passengers)
            {
                Pose(passenger, 0);
            }

            world.Advance(TimeSpan.FromMilliseconds(600));
        }

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Van)!.OwnerSmallId);
        Assert.Equal(10f, world.Server.Entities.Get(Van)!.X);
        Assert.True(world.AgreeOnOwner(Van), string.Join(", ", world.OwnersOf(Van)));
        Assert.All(world.OwnersOf(Van).Values, owner => Assert.Equal(driver.SmallId, owner));

        for (var i = 0; i < world.Players.Count; i++)
        {
            foreach (var passenger in passengers)
            {
                Assert.Equal(0, AnnouncementsNaming(world, world.Players[i], passenger.SmallId, before[i]));
            }
        }
    }

    [Fact]
    public void Only_a_driver_seat_is_ever_logged_as_taking_the_van()
    {
        var (world, driver, passengers) = VanWithThree(driverLock: false);
        using var _ = world;

        foreach (var passenger in passengers)
        {
            Pose(passenger, 0);
            world.Advance(TimeSpan.FromMilliseconds(600));
        }

        Pose(driver, 1);

        var taken = world.Server.RecentLog(2000)
            .Where(e => e.Message.Contains("who sits in it in seat")
                && !e.Message.EndsWith("who sits in it in seat 0"))
            .ToList();

        Assert.Empty(taken);
    }

    [Fact]
    public void The_drivers_poses_are_never_ignored_while_passengers_send_theirs()
    {
        var (world, driver, passengers) = VanWithThree(driverLock: false);
        using var _ = world;

        for (var tick = 1; tick <= 10; tick++)
        {
            foreach (var passenger in passengers)
            {
                Pose(passenger, 0);
            }

            Pose(driver, tick);
            world.Advance(TimeSpan.FromMilliseconds(600));
        }

        var ignored = world.Server.RecentLog(2000)
            .Where(e => e.Message.StartsWith($"Ignored a pose for entity {Van}")
                && e.Message.Contains($"from {driver.Name}"))
            .ToList();

        Assert.Empty(ignored);
    }

    [Fact]
    public void A_driver_who_gets_out_keeps_the_van_until_a_rider_asks_for_it()
    {
        var (world, driver, passengers) = VanWithThree(driverLock: false);
        using var _ = world;
        Pose(driver, 1);

        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Van, 0, false));
        world.Advance(TimeSpan.FromMilliseconds(600));

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Van)!.OwnerSmallId);

        passengers[0].Send(FusionProtocol.BuildOwnershipRequest(passengers[0].SmallId, Van));

        Assert.Equal(passengers[0].SmallId, world.Server.Entities.Get(Van)!.OwnerSmallId);
        Assert.True(world.AgreeOnOwner(Van), string.Join(", ", world.OwnersOf(Van)));
    }

    [Fact]
    public void A_van_whose_owner_leaves_goes_to_the_driver_and_not_the_first_passenger()
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = false, OwnershipRequestsPerSecond = 0 });
        using var _ = world;
        var outsider = world.Join(76561198000000004, "Siriuss");
        var driver = world.Join(76561198000000001, "s1mple");
        var passenger = world.Join(76561198000000002, "PokeMrowa");

        foreach (var player in world.Players)
        {
            player.FinishLoading();
        }

        world.Spawn(outsider, Van, "BaBaCorp.AssortedAutomobiles.Spawnable.VanSWATTransport", 0, 0, 0);

        passenger.Send(FusionProtocol.BuildSeat(passenger.SmallId, Van, 2, true));
        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Van, 0, true));

        world.Leave(outsider, "Closing Connection");

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Van)!.OwnerSmallId);
        Assert.All(world.OwnersOf(Van).Values, owner => Assert.Equal(driver.SmallId, owner));
    }
}
