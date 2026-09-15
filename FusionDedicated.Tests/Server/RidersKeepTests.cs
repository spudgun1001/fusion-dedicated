using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A vehicle somebody sits in stays with its riders. Everybody bumping into it asked for it on
/// every impact, and the owner went round between them thousands of times a second.
/// </summary>
public class RidersKeepTests
{
    private static readonly DateTime Seated = new(2026, 9, 15, 19, 14, 0, DateTimeKind.Utc);

    private static SeatRecord Rider(byte rider, byte index) => new(rider, 2365, index, Seated);

    [Fact]
    public void A_vehicle_nobody_sits_in_keeps_nothing()
        => Assert.False(WorldCatchup.RidersKeep(7, Array.Empty<SeatRecord>()));

    [Fact]
    public void Somebody_outside_is_kept_out()
        => Assert.True(WorldCatchup.RidersKeep(7, new[] { Rider(2, 0) }));

    [Fact]
    public void The_driver_is_not_kept_out()
        => Assert.False(WorldCatchup.RidersKeep(2, new[] { Rider(2, 0) }));

    [Fact]
    public void A_passenger_is_not_kept_out()
        => Assert.False(WorldCatchup.RidersKeep(3, new[] { Rider(2, 0), Rider(3, 1) }));

    [Fact]
    public void A_passenger_alone_keeps_somebody_outside_out()
        => Assert.True(WorldCatchup.RidersKeep(7, new[] { Rider(3, 1) }));

    [Fact]
    public void The_rider_check_comes_before_the_driver_check_and_the_repeat_check()
    {
        string method = FusionServerSource.Method("private void HandleOwnershipRequest(");

        int riders = method.IndexOf("WorldCatchup.RidersKeep(", StringComparison.Ordinal);
        int driver = method.IndexOf("WorldCatchup.DriverKeeps(", StringComparison.Ordinal);
        int repeat = method.IndexOf("_confirmations.ShouldConfirm(", StringComparison.Ordinal);

        Assert.True(riders > 0, "no rider check");
        Assert.True(riders > method.IndexOf("MayHold(sender, entityId)", StringComparison.Ordinal));
        Assert.True(riders < driver && driver < repeat);
    }

    [Fact]
    public void The_rider_check_reads_the_seat_book()
        => Assert.Contains("_seats.RidersOf(entityId)", FusionServerSource.Method("private void HandleOwnershipRequest("));

    [Fact]
    public void Leaving_forgets_when_a_players_refusal_was_last_logged()
        => Assert.Contains("_seatRefusalLog.Remove(player.SmallId);", FusionServerSource.Method("private void Depart("));
}
