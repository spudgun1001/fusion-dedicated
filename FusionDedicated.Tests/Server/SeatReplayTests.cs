using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Telling a client who sits in a vehicle it has just asked about. Fusion's own
/// answer seats whoever answered instead of the rider, which put riders on the
/// hood for anybody who arrived after they sat.
/// </summary>
public class SeatReplayTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SeatRecord Driver = new(3, 300, 0, Now);
    private static readonly SeatRecord Passenger = new(4, 300, 1, Now.AddSeconds(2));
    private static readonly SeatRecord Elsewhere = new(5, 400, 0, Now.AddSeconds(1));

    private static bool Everyone(byte smallId) => true;

    [Fact]
    public void Every_rider_is_replayed_when_asked_about_that_vehicle()
        => Assert.Equal(new[] { Driver, Passenger },
            WorldCatchup.SeatsToReplay(new[] { Driver, Elsewhere, Passenger }, 300, 9, Everyone));

    [Fact]
    public void The_one_asking_is_not_seated_by_the_replay()
        => Assert.Equal(new[] { Driver },
            WorldCatchup.SeatsToReplay(new[] { Driver, Passenger }, 300, requester: 4, Everyone));

    [Fact]
    public void A_rider_who_has_left_is_not_replayed()
        => Assert.Equal(new[] { Passenger },
            WorldCatchup.SeatsToReplay(new[] { Driver, Passenger }, 300, 9, present: id => id != 3));

    [Fact]
    public void A_vehicle_nobody_sits_in_replays_nothing()
        => Assert.Empty(WorldCatchup.SeatsToReplay(Array.Empty<SeatRecord>(), 300, 9, Everyone));

    [Fact]
    public void Records_for_other_vehicles_are_ignored()
        => Assert.Empty(WorldCatchup.SeatsToReplay(new[] { Driver, Passenger }, 400, 9, Everyone));

    [Fact]
    public void A_data_request_replays_each_seat_as_its_rider()
    {
        Assert.Contains("ReplaySeats(sender, request.EntityId);",
            FusionServerSource.Method("private void HandleEntityDataRequest("));

        string replay = FusionServerSource.Method("private void ReplaySeats(");

        Assert.Contains("FusionProtocol.BuildSeat(seat.Rider, entityId, seat.Index, true)", replay);
        Assert.Contains("SendTo(requester.Connection", replay);
    }
}
