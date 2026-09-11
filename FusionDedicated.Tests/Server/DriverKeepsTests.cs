using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A vehicle stays with whoever drives it. Granting it to somebody who bumped into
/// it set the owner bouncing between them and the driver thousands of times. A car
/// with no driver lock can end up owned by a passenger, so a rider holding the car,
/// which is the wheel, takes it from a rider who is not.
/// </summary>
public class DriverKeepsTests
{
    private static readonly byte[] Nobody = Array.Empty<byte>();

    [Fact]
    public void A_driver_keeps_the_vehicle_they_sit_in()
        => Assert.True(WorldCatchup.DriverKeeps(7, owner: 2, ownerSeatEntity: 425, requesterSeatEntity: null, Nobody, 425));

    [Fact]
    public void A_driver_asking_for_it_themselves_is_not_stopped()
        => Assert.False(WorldCatchup.DriverKeeps(2, owner: 2, ownerSeatEntity: 425, requesterSeatEntity: 425, Nobody, 425));

    [Fact]
    public void An_owner_sitting_in_something_else_does_not_keep_this()
        => Assert.False(WorldCatchup.DriverKeeps(7, owner: 2, ownerSeatEntity: 999, requesterSeatEntity: null, Nobody, 425));

    [Fact]
    public void An_owner_sitting_nowhere_does_not_keep_it()
        => Assert.False(WorldCatchup.DriverKeeps(7, owner: 2, ownerSeatEntity: null, requesterSeatEntity: null, Nobody, 425));

    [Fact]
    public void Nobody_owning_it_keeps_nothing()
        => Assert.False(WorldCatchup.DriverKeeps(7, owner: null, ownerSeatEntity: 425, requesterSeatEntity: 425, Nobody, 425));

    [Fact]
    public void A_rider_holding_the_wheel_takes_it_from_a_rider_who_is_not()
        => Assert.False(WorldCatchup.DriverKeeps(7, owner: 2, ownerSeatEntity: 425, requesterSeatEntity: 425, new byte[] { 7 }, 425));

    [Fact]
    public void A_rider_not_holding_it_does_not_take_it()
        => Assert.True(WorldCatchup.DriverKeeps(7, owner: 2, ownerSeatEntity: 425, requesterSeatEntity: 425, Nobody, 425));

    [Fact]
    public void A_rider_holding_it_does_not_take_it_from_an_owner_also_holding_it()
        => Assert.True(WorldCatchup.DriverKeeps(7, owner: 2, ownerSeatEntity: 425, requesterSeatEntity: 425, new byte[] { 2, 7 }, 425));

    [Fact]
    public void Somebody_outside_holding_it_does_not_take_it()
        => Assert.True(WorldCatchup.DriverKeeps(7, owner: 2, ownerSeatEntity: 425, requesterSeatEntity: null, new byte[] { 7 }, 425));

    [Fact]
    public void An_ownership_request_checks_for_a_driver_before_granting()
    {
        string method = FusionServerSource.Method("private void HandleOwnershipRequest(");

        int check = method.IndexOf("WorldCatchup.DriverKeeps(", StringComparison.Ordinal);

        Assert.True(check > 0, "no driver check");
        Assert.True(check < method.IndexOf("Plugins?.Ownership.Raise(", StringComparison.Ordinal));
        Assert.True(check < method.IndexOf("Entities.SetOwner(entityId, requestedOwner);", StringComparison.Ordinal));
    }

    [Fact]
    public void The_driver_check_knows_who_sits_and_who_holds()
    {
        string method = FusionServerSource.Method("private void HandleOwnershipRequest(");

        Assert.Contains("_seats.SeatOf(sender.SmallId)?.EntityId", method);
        Assert.Contains("_grabs.HoldersOf(entityId)", method);
    }

    [Fact]
    public void The_asker_is_told_the_driver_owns_it()
        => Assert.Contains("FusionProtocol.BuildOwnershipResponse(driver, entityId)",
            FusionServerSource.Method("private void HandleOwnershipRequest("));
}
