using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Who sits where. Fusion sends a seat once and never again, so this is the only
/// record a player who joins later can be told from.
/// </summary>
public class SeatBookTests
{
    private static readonly DateTime Now = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_rider_is_remembered_in_their_seat()
    {
        var book = new SeatBook();
        book.Ingress(3, 300, 1, Now);

        Assert.Equal(new SeatRecord(3, 300, 1, Now), book.SeatOf(3));
        Assert.True(book.IsOccupied(300));
    }

    [Fact]
    public void Sitting_somewhere_else_leaves_the_first_seat()
    {
        var book = new SeatBook();
        book.Ingress(3, 300, 0, Now);
        book.Ingress(3, 400, 1, Now.AddSeconds(5));

        Assert.Equal((ushort)400, book.SeatOf(3)!.Value.EntityId);
        Assert.False(book.IsOccupied(300));
        Assert.Single(book.All());
    }

    [Fact]
    public void A_second_rider_in_the_same_seat_replaces_the_first()
    {
        // Two players cannot share a seat, so the older record is the stale one.
        var book = new SeatBook();
        book.Ingress(3, 300, 0, Now);
        book.Ingress(4, 300, 0, Now.AddSeconds(1));

        Assert.Null(book.SeatOf(3));
        Assert.Equal((byte)4, book.RidersOf(300).Single().Rider);
    }

    [Fact]
    public void Sitting_again_in_the_same_seat_keeps_the_first_place_in_the_order()
    {
        var book = new SeatBook();
        book.Ingress(3, 300, 0, Now);
        book.Ingress(4, 300, 1, Now.AddSeconds(1));
        book.Ingress(3, 300, 0, Now.AddSeconds(2));

        Assert.Equal(new byte[] { 3, 4 }, book.RidersOf(300).Select(s => s.Rider));
        Assert.Equal(Now, book.SeatOf(3)!.Value.SeatedUtc);
    }

    [Fact]
    public void Getting_out_empties_the_seat()
    {
        var book = new SeatBook();
        book.Ingress(3, 300, 0, Now);

        Assert.True(book.Egress(3));
        Assert.Null(book.SeatOf(3));
        Assert.False(book.IsOccupied(300));
        Assert.False(book.Egress(3));
    }

    [Fact]
    public void A_rider_who_leaves_is_forgotten()
    {
        var book = new SeatBook();
        book.Ingress(3, 300, 0, Now);
        book.Ingress(4, 300, 1, Now);

        Assert.Equal(1, book.ForgetRider(3));
        Assert.Null(book.SeatOf(3));
        Assert.Equal(new byte[] { 4 }, book.RidersOf(300).Select(s => s.Rider));
    }

    [Fact]
    public void A_vehicle_that_goes_takes_its_seats_with_it()
    {
        var book = new SeatBook();
        book.Ingress(3, 300, 0, Now);
        book.Ingress(4, 300, 1, Now);
        book.Ingress(5, 400, 0, Now);

        Assert.Equal(2, book.ForgetEntity(300));
        Assert.False(book.IsOccupied(300));
        Assert.Null(book.SeatOf(3));
        Assert.Equal((ushort)400, book.SeatOf(5)!.Value.EntityId);
    }

    [Fact]
    public void Riders_are_listed_in_the_order_they_sat()
    {
        var book = new SeatBook();
        book.Ingress(6, 300, 2, Now);
        book.Ingress(3, 300, 0, Now.AddSeconds(1));
        book.Ingress(9, 400, 0, Now.AddSeconds(2));
        book.Ingress(4, 300, 1, Now.AddSeconds(3));

        Assert.Equal(new byte[] { 6, 3, 4 }, book.RidersOf(300).Select(s => s.Rider));
    }

    [Fact]
    public void A_seat_is_recorded_only_while_somebody_sits_in_it()
    {
        var book = new SeatBook();
        book.Ingress(3, 300, 1, Now);

        Assert.True(book.IsRecorded(300, 1));
        Assert.False(book.IsRecorded(300, 0));
        Assert.False(book.IsRecorded(400, 1));

        book.Egress(3);

        Assert.False(book.IsRecorded(300, 1));
    }

    [Theory]
    [InlineData(0f, 0f, 0f, false)]
    [InlineData(14.9f, 0f, 0f, false)]
    [InlineData(15f, 0f, 0f, false)]
    [InlineData(15.1f, 0f, 0f, true)]
    [InlineData(10f, 10f, 10f, true)]
    [InlineData(0f, -20f, 0f, true)]
    public void A_rider_is_stale_only_beyond_fifteen_metres(float x, float y, float z, bool stale)
        => Assert.Equal(stale, SeatBook.IsStale(100f + x, 5f + y, -40f + z, 100f, 5f, -40f));
}
