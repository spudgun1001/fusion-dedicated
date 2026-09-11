using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A vehicle stays with whoever drives it. Granting it to somebody who bumped into
/// it set the owner bouncing between them and the driver thousands of times.
/// </summary>
public class DriverKeepsTests
{
    [Fact]
    public void A_driver_keeps_the_vehicle_they_sit_in()
        => Assert.True(WorldCatchup.DriverKeeps(requester: 7, owner: 2, ownerSeatEntity: 425, entity: 425));

    [Fact]
    public void A_driver_asking_for_it_themselves_is_not_stopped()
        => Assert.False(WorldCatchup.DriverKeeps(requester: 2, owner: 2, ownerSeatEntity: 425, entity: 425));

    [Fact]
    public void An_owner_sitting_in_something_else_does_not_keep_this()
        => Assert.False(WorldCatchup.DriverKeeps(requester: 7, owner: 2, ownerSeatEntity: 999, entity: 425));

    [Fact]
    public void An_owner_sitting_nowhere_does_not_keep_it()
        => Assert.False(WorldCatchup.DriverKeeps(requester: 7, owner: 2, ownerSeatEntity: null, entity: 425));

    [Fact]
    public void Nobody_owning_it_keeps_nothing()
        => Assert.False(WorldCatchup.DriverKeeps(requester: 7, owner: null, ownerSeatEntity: 425, entity: 425));

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
    public void The_asker_is_told_the_driver_owns_it()
        => Assert.Contains("FusionProtocol.BuildOwnershipResponse(driver, entityId)",
            FusionServerSource.Method("private void HandleOwnershipRequest("));
}
