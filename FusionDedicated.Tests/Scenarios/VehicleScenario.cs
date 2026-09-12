using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Scenarios;

/// <summary>Scenario 3: a driver, a passenger and a bystander who keeps bumping the car.</summary>
public class VehicleScenario
{
    private const ushort Car = 400;

    private static (World World, FakePlayer Driver, FakePlayer Passenger, FakePlayer Bystander) Car_with_three(bool driverLock)
    {
        var world = new World();
        var driver = world.Join(76561198000000001, "s1mple");
        var passenger = world.Join(76561198000000002, "PokeMrowa");
        var bystander = world.Join(76561198000000003, "FOLZY");

        foreach (var player in world.Players)
        {
            player.FinishLoading();

            if (driverLock)
            {
                player.View.DriverLockedVehicles.Add(Car);
            }
        }

        world.Spawn(driver, Car, "BaBaCorp.AssortedAutomobiles.Spawnable.SendalSopperSedan", 0, 0, 0);
        world.Sync();

        return (world, driver, passenger, bystander);
    }

    private static void Drive(FakePlayer driver)
        => driver.Send(FusionProtocol.BuildEntityPoseUpdate(driver.SmallId, Car, new Vec3(1, 0, 0), default, default, default));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_bystander_bumping_a_driven_car_does_not_take_it(bool driverLock)
    {
        var (world, driver, passenger, bystander) = Car_with_three(driverLock);
        using var _ = world;

        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));
        passenger.Send(FusionProtocol.BuildSeat(passenger.SmallId, Car, 1, true));
        Drive(driver);

        for (var bump = 0; bump < 20; bump++)
        {
            bystander.Send(FusionProtocol.BuildOwnershipRequest(bystander.SmallId, Car));

            Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
            Assert.True(world.AgreeOnOwner(Car), $"bump {bump}: " + string.Join(", ", world.OwnersOf(Car)));

            Drive(driver);
        }

        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));
        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.All(world.Players, p => Assert.Equal(0, p.View.PosesRejected));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_passenger_who_sat_first_does_not_keep_the_car_from_the_driver(bool driverLock)
    {
        var (world, driver, passenger, _) = Car_with_three(driverLock);
        using var __ = world;

        passenger.Send(FusionProtocol.BuildSeat(passenger.SmallId, Car, 1, true));
        passenger.Send(FusionProtocol.BuildOwnershipRequest(passenger.SmallId, Car));

        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));
        driver.Send(FusionProtocol.BuildGrab(driver.SmallId, FusionProtocol.Handedness.RIGHT, 0, Car));
        driver.Send(FusionProtocol.BuildOwnershipRequest(driver.SmallId, Car));

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);

        Drive(driver);

        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));
        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Riders_are_in_their_seats_for_a_player_who_joins_mid_ride(bool driverLock)
    {
        var (world, driver, passenger, _) = Car_with_three(driverLock);
        using var __ = world;

        // The passenger has the car before anyone sits in it.
        passenger.Send(FusionProtocol.BuildOwnershipRequest(passenger.SmallId, Car));

        var late = world.Join(76561198000000004, "Late");

        if (driverLock)
        {
            late.View.DriverLockedVehicles.Add(Car);
        }

        // The ride starts while Late is still loading, so the owner moves to the driver out of Late's sight.
        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));
        passenger.Send(FusionProtocol.BuildSeat(passenger.SmallId, Car, 1, true));
        Drive(driver);

        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal((Car, (byte)0), late.View.Seats[driver.SmallId]);
        Assert.Equal((Car, (byte)1), late.View.Seats[passenger.SmallId]);
        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));
    }
}
