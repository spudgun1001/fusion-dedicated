using FusionDedicated;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>How long an entity stays with its new owner before it may change hands again.</summary>
public class OwnershipHoldTests
{
    private static readonly DateTime Start = new(2026, 9, 18, 18, 35, 7, DateTimeKind.Utc);

    private DateTime _now = Start;
    private int _window = 500;

    private OwnershipHold Hold() => new(() => _now, () => _window);

    [Fact]
    public void The_defaults_are_half_a_second_and_ten_requests_a_second()
    {
        var config = new ServerConfig();

        Assert.Equal(500, config.OwnershipHoldMilliseconds);
        Assert.Equal(10, config.OwnershipRequestsPerSecond);
    }

    [Fact]
    public void An_entity_nobody_has_taken_is_not_held()
        => Assert.False(Hold().Holding(1820));

    [Fact]
    public void A_change_inside_the_window_is_held()
    {
        var hold = Hold();
        hold.Note(1820);

        _now = Start.AddMilliseconds(499);

        Assert.True(hold.Holding(1820));
    }

    [Fact]
    public void A_change_after_the_window_is_not()
    {
        var hold = Hold();
        hold.Note(1820);

        _now = Start.AddMilliseconds(500);

        Assert.False(hold.Holding(1820));
    }

    [Fact]
    public void Another_entity_is_not_held()
    {
        var hold = Hold();
        hold.Note(1820);

        Assert.False(hold.Holding(1322));
    }

    [Fact]
    public void A_window_of_zero_holds_nothing()
    {
        var hold = Hold();
        hold.Note(1820);
        _window = 0;

        Assert.False(hold.Holding(1820));
    }

    [Fact]
    public void A_removed_entity_is_forgotten()
    {
        var hold = Hold();
        hold.Note(1820);
        hold.ForgetEntity(1820);

        Assert.False(hold.Holding(1820));
    }

    [Fact]
    public void A_clock_that_went_backwards_does_not_hold_forever()
    {
        var hold = Hold();
        hold.Note(1820);

        _now = Start.AddMinutes(-5);

        Assert.False(hold.Holding(1820));
    }
}
