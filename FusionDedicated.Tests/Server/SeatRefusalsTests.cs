using FusionDedicated;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>How long a rider refused a seat is left alone before the plugin is asked again.</summary>
public class SeatRefusalsTests
{
    private static readonly DateTime Start = new(2026, 9, 18, 17, 36, 0, DateTimeKind.Utc);

    private const byte Siriuss = 3;
    private const byte Dennis = 4;
    private const ushort Suv = 893;
    private const ushort Van = 1820;

    private DateTime _now = Start;
    private double _cooldown = 2;

    private SeatRefusals Refusals() => new(() => _now, () => _cooldown);

    [Fact]
    public void The_default_cooldown_is_two_seconds()
        => Assert.Equal(2, new ServerConfig().SeatRefusalCooldownSeconds);

    [Fact]
    public void A_rider_who_has_not_been_refused_is_not_cooling()
        => Assert.False(Refusals().Cooling(Siriuss, Suv, 0));

    [Fact]
    public void An_attempt_inside_the_window_is_cooling()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);

        _now = Start.AddMilliseconds(1999);

        Assert.True(refusals.Cooling(Siriuss, Suv, 0));
    }

    [Fact]
    public void An_attempt_after_the_window_is_not()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);

        _now = Start.AddSeconds(2);

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));
    }

    [Fact]
    public void Another_seat_entity_or_rider_is_not_cooling()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);

        Assert.False(refusals.Cooling(Siriuss, Suv, 1));
        Assert.False(refusals.Cooling(Siriuss, Van, 0));
        Assert.False(refusals.Cooling(Dennis, Suv, 0));
    }

    [Fact]
    public void A_cooldown_of_zero_or_less_cools_nothing()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);
        _cooldown = 0;

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));

        _cooldown = -1;

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));
    }

    [Fact]
    public void A_clock_that_went_backwards_ends_the_cooldown()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);

        _now = Start.AddSeconds(-1);

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));
    }

    [Fact]
    public void The_next_refusal_is_told_how_many_attempts_were_dropped()
    {
        var refusals = Refusals();
        Assert.Equal(0, refusals.Note(Siriuss, Suv, 0));

        refusals.Cooling(Siriuss, Suv, 0);
        refusals.Cooling(Siriuss, Suv, 0);
        _now = Start.AddSeconds(2);

        Assert.Equal(2, refusals.Note(Siriuss, Suv, 0));
        Assert.Equal(0, refusals.Note(Siriuss, Suv, 0));
    }

    [Fact]
    public void A_rider_is_remembered_for_no_more_seats_than_the_cap()
    {
        var refusals = Refusals();

        for (var seat = 0; seat <= SeatRefusals.SeatsPerRider; seat++)
        {
            refusals.Note(Siriuss, Suv, (byte)seat);
            _now = _now.AddMilliseconds(1);
        }

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));
        Assert.True(refusals.Cooling(Siriuss, Suv, SeatRefusals.SeatsPerRider));
    }

    [Fact]
    public void A_rider_who_left_is_forgotten()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);
        refusals.Note(Dennis, Suv, 0);

        refusals.ForgetRider(Siriuss);

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));
        Assert.True(refusals.Cooling(Dennis, Suv, 0));
    }

    [Fact]
    public void A_vehicle_that_has_gone_is_forgotten()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);
        refusals.Note(Siriuss, Van, 0);

        refusals.ForgetEntity(Suv);

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));
        Assert.True(refusals.Cooling(Siriuss, Van, 0));
    }

    [Fact]
    public void A_new_level_forgets_everything()
    {
        var refusals = Refusals();
        refusals.Note(Siriuss, Suv, 0);
        refusals.Note(Dennis, Van, 3);

        refusals.Clear();

        Assert.False(refusals.Cooling(Siriuss, Suv, 0));
        Assert.False(refusals.Cooling(Dennis, Van, 3));
    }
}
