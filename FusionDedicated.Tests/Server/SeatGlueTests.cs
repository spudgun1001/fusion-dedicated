using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Seats as they pass through the server: kept, and passed on as before, except
/// Fusion's catch-up reply for a seat the server already has.
/// </summary>
public class SeatGlueTests
{
    [Theory]
    [InlineData((byte)2, true, true)]
    [InlineData((byte)3, true, true)]
    [InlineData((byte)3, true, false)]
    [InlineData((byte)3, false, true)]
    [InlineData((byte)4, false, true)]
    [InlineData((byte)4, true, false)]
    public void Everything_but_a_catch_up_reply_for_a_known_seat_is_passed_on(
        byte relayType, bool ingress, bool recorded)
        => Assert.True(WorldCatchup.PassSeatMessage(relayType, ingress, recorded));

    [Fact]
    public void A_catch_up_reply_for_a_seat_the_server_has_is_dropped()
    {
        // It is stamped with whoever answered, so passing it on seats them in the
        // rider's place. The server replays the right rider instead.
        Assert.False(WorldCatchup.PassSeatMessage(4, ingress: true, seatRecorded: true));
    }

    [Fact]
    public void A_seat_message_is_handled_and_still_passed_on()
    {
        string loop = FusionServerSource.Method("private void HandleMessage(");

        int seatCase = loop.IndexOf("case FusionProtocol.TagPlayerRepSeat when sender != null:", StringComparison.Ordinal);
        int handled = loop.IndexOf("HandleSeat(sender, message);", StringComparison.Ordinal);

        Assert.True(seatCase > 0, "tag 8 is no longer handled in the message loop");
        Assert.True(handled > seatCase);

        string seat = FusionServerSource.Method("private void HandleSeat(");

        Assert.Contains("WorldCatchup.PassSeatMessage(", seat);
        Assert.Contains("Relay(sender, message);", seat);
    }

    [Fact]
    public void A_live_seat_is_kept_and_so_is_getting_out()
    {
        string seat = FusionServerSource.Method("private void HandleSeat(");

        // Either straight on the book or through a wrapper that also keeps
        // occupancy in step, so the receiver is left out of the match.
        Assert.Contains("Ingress(sender.SmallId, seat.SeatId, seat.Index, DateTime.UtcNow);", seat);
        Assert.Contains("Egress(sender.SmallId);", seat);
    }

    [Fact]
    public void Seats_are_forgotten_with_the_rider_and_with_the_vehicle()
    {
        Assert.Contains("ForgetRider(player.SmallId);", FusionServerSource.Method("private void Depart("));
        Assert.Contains("Entities.Removed += id => _seats.ForgetEntity(id);",
            FusionServerSource.Method("public FusionServer(ServerConfig config)"));
    }

    [Fact]
    public void A_rider_far_from_their_seat_is_taken_out_of_it()
    {
        string pose = FusionServerSource.Method("private void TrackPlayerPose(");

        Assert.Contains("SeatBook.IsStale(", pose);
        Assert.Contains("Egress(sender.SmallId);", pose);
        Assert.Contains("vehicle.PositionKnown", pose);
    }

    [Theory]
    [InlineData((byte)3, true, true)]
    [InlineData((byte)2, true, true)]
    public void A_live_ingress_into_a_known_entity_is_kept(byte relayType, bool ingress, bool known)
        => Assert.True(WorldCatchup.KeepSeat(relayType, ingress, known));

    [Fact]
    public void A_catch_up_reply_ingress_is_not_kept()
        => Assert.False(WorldCatchup.KeepSeat(4, ingress: true, known: true));

    [Fact]
    public void An_ingress_into_an_unknown_entity_is_not_kept()
        => Assert.False(WorldCatchup.KeepSeat(3, ingress: true, known: false));

    [Fact]
    public void An_egress_is_not_kept()
        => Assert.False(WorldCatchup.KeepSeat(3, ingress: false, known: true));

    [Fact]
    public void HandleSeat_decides_through_KeepSeat()
        => Assert.Contains("WorldCatchup.KeepSeat(", FusionServerSource.Method("private void HandleSeat("));

    [Fact]
    public void An_unqueued_entity_starts_with_no_known_position()
        => Assert.Contains("PositionKnown = false;", FusionServerSource.Method("private void HandleUnqueueRequest("));

    [Theory]
    [InlineData((byte)2, true)]
    [InlineData((byte)3, true)]
    [InlineData((byte)0, false)]
    [InlineData((byte)1, false)]
    [InlineData((byte)4, false)]
    [InlineData((byte)5, false)]
    public void Only_relay_types_2_and_3_are_live_seats(byte relayType, bool live)
        => Assert.Equal(live, WorldCatchup.IsLiveSeat(relayType));

    [Fact]
    public void HandleSeat_only_egresses_an_unkept_seat_when_it_is_live()
        => Assert.Contains("else if (WorldCatchup.IsLiveSeat(seat.RelayType))",
            FusionServerSource.Method("private void HandleSeat("));

    [Theory]
    [InlineData("private void SeatIngress(")]
    [InlineData("private bool SeatEgress(")]
    [InlineData("private int SeatForgetRider(")]
    public void Each_seat_wrapper_takes_the_seat_lock(string wrapper)
    {
        // Depart can run off the message loop through Kick.
        Assert.Contains("lock (_seatLock)", FusionServerSource.Method(wrapper));
    }

    [Theory]
    [InlineData("_seats.Ingress(", "private void SeatIngress(")]
    [InlineData("_seats.Egress(", "private bool SeatEgress(")]
    [InlineData("_seats.ForgetRider(", "private int SeatForgetRider(")]
    public void The_seat_book_is_only_changed_through_its_wrappers(string call, string wrapper)
    {
        Assert.Equal(1, Occurrences(FusionServerSource.Method(wrapper), call));
        Assert.True(Occurrences(FusionServerSource.Text(), call) == 1,
            $"'{call}' is called outside {wrapper}, which skips the seat lock and the occupancy sync");
    }

    private static int Occurrences(string text, string value)
    {
        int count = 0;

        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
