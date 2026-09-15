using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Riding or bumping a car somebody else drives sends an ownership request on every impact.
/// While anybody sits in it only its riders may take it, and nobody else is answered at all.
/// </summary>
public class SeatedVehicleOwnershipTests
{
    private const ushort Car = 400;

    private static (World World, FakePlayer Driver, FakePlayer Passenger, FakePlayer Bystander) CarOwnedByDriver()
    {
        var world = new World();
        var driver = world.Join(76561198000000001, "s1mple");
        var passenger = world.Join(76561198000000002, "PokeMrowa");
        var bystander = world.Join(76561198000000003, "FOLZY");

        foreach (var player in world.Players)
        {
            player.FinishLoading();
        }

        world.Spawn(driver, Car, "BaBaCorp.AssortedAutomobiles.Spawnable.SendalSopperSedan", 0, 0, 0);

        return (world, driver, passenger, bystander);
    }

    private static int[] SentCounts(World world)
        => world.Players.Select(p => world.Transport.SentTo(p.Connection).Count).ToArray();

    private static int OwnershipResponsesTo(World world, FakePlayer player, int from)
        => world.Transport.SentTo(player.Connection)
            .Skip(from)
            .Count(sent => FusionProtocol.TryReadOwnershipResponse(sent.Message) is { } response
                && response.EntityId == Car);

    private static void Ask(FakePlayer player)
        => player.Send(FusionProtocol.BuildOwnershipRequest(player.SmallId, Car));

    [Fact]
    public void A_bystander_asking_while_the_driver_sits_changes_nothing_and_nobody_is_sent_anything()
    {
        var (world, driver, _, bystander) = CarOwnedByDriver();
        using var __ = world;
        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));
        var before = SentCounts(world);

        for (var bump = 0; bump < 20; bump++)
        {
            Ask(bystander);
        }

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.Equal(before, SentCounts(world));
        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));
    }

    [Fact]
    public void A_passenger_not_holding_the_wheel_is_refused_without_an_answer()
    {
        var (world, driver, passenger, _) = CarOwnedByDriver();
        using var __ = world;
        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));
        passenger.Send(FusionProtocol.BuildSeat(passenger.SmallId, Car, 1, true));
        var before = SentCounts(world);

        Ask(passenger);

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.Equal(before, SentCounts(world));
    }

    [Fact]
    public void With_only_a_passenger_seated_a_bystander_is_refused_without_an_answer()
    {
        var (world, driver, passenger, bystander) = CarOwnedByDriver();
        using var __ = world;
        passenger.Send(FusionProtocol.BuildSeat(passenger.SmallId, Car, 1, true));
        var before = SentCounts(world);

        Ask(bystander);

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.Equal(before, SentCounts(world));
    }

    [Fact]
    public void With_only_a_passenger_seated_the_passenger_may_take_it()
    {
        var (world, driver, passenger, bystander) = CarOwnedByDriver();
        using var __ = world;
        passenger.Send(FusionProtocol.BuildSeat(passenger.SmallId, Car, 1, true));
        int driverBefore = world.Transport.SentTo(driver.Connection).Count;
        int bystanderBefore = world.Transport.SentTo(bystander.Connection).Count;

        Ask(passenger);

        Assert.Equal(passenger.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.Equal(1, OwnershipResponsesTo(world, driver, driverBefore));
        Assert.Equal(1, OwnershipResponsesTo(world, bystander, bystanderBefore));
        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));
    }

    [Fact]
    public void A_car_nobody_sits_in_goes_to_whoever_asks_and_everybody_is_told()
    {
        var (world, driver, passenger, bystander) = CarOwnedByDriver();
        using var __ = world;
        int driverBefore = world.Transport.SentTo(driver.Connection).Count;
        int passengerBefore = world.Transport.SentTo(passenger.Connection).Count;

        Ask(bystander);

        Assert.Equal(bystander.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.Equal(1, OwnershipResponsesTo(world, driver, driverBefore));
        Assert.Equal(1, OwnershipResponsesTo(world, passenger, passengerBefore));
        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));
    }

    [Fact]
    public void A_bystander_refused_many_times_is_logged_once()
    {
        var (world, driver, _, bystander) = CarOwnedByDriver();
        using var __ = world;
        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));

        for (var bump = 0; bump < 20; bump++)
        {
            Ask(bystander);
        }

        Assert.Equal(1, world.Server.RecentLog(2000)
            .Count(e => e.Message.StartsWith($"Refused FOLZY ownership of entity {Car}")));
    }
}
