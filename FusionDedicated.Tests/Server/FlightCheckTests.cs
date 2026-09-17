using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Telling a flight from the ways a player legitimately moves up and down: jumps, ladders,
/// lifts, and a long drop off a building, which reaches a steady speed like a flight does.
/// </summary>
public class FlightCheckTests
{
    private static readonly DateTime Start = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);

    private static ServerConfig Config() => new();

    /// <summary>Feeds heights at Fusion's 20 poses a second and returns the first flight it calls.</summary>
    private static FlightVerdict Fly(FlightCheck check, Func<float, float> heightAt, float seconds, byte player = 1)
    {
        var last = new FlightVerdict(FlightKind.None, 0f);

        for (var step = 0; step <= (int)(seconds * 20); step++)
        {
            float at = step / 20f;
            var verdict = check.Note(player, heightAt(at), Start.AddSeconds(at));

            if (verdict.Kind != FlightKind.None && last.Kind == FlightKind.None)
            {
                last = verdict;
            }
        }

        return last;
    }

    [Fact]
    public void A_steady_climb_is_a_flight()
    {
        var verdict = Fly(new FlightCheck(Config()), at => at * 8f, 4f);

        Assert.Equal(FlightKind.Climb, verdict.Kind);
        Assert.Equal(8f, verdict.Speed, 1);
    }

    [Fact]
    public void A_steady_descent_is_a_flight()
    {
        var verdict = Fly(new FlightCheck(Config()), at => 100f - at * 8f, 4f);

        Assert.Equal(FlightKind.Descent, verdict.Kind);
        Assert.Equal(8f, verdict.Speed, 1);
    }

    [Fact]
    public void A_jump_is_not_a_flight()
    {
        // Straight up at 5 m/s and back down under gravity: about 1.3 m in half a second.
        var verdict = Fly(new FlightCheck(Config()), at => Math.Max(0f, 5f * at - 4.9f * at * at), 4f);

        Assert.Equal(FlightKind.None, verdict.Kind);
    }

    [Fact]
    public void A_ladder_or_a_lift_is_not_a_flight()
    {
        Assert.Equal(FlightKind.None, Fly(new FlightCheck(Config()), at => at * 1.2f, 10f).Kind);
        Assert.Equal(FlightKind.None, Fly(new FlightCheck(Config()), at => at * 3f, 10f).Kind);
    }

    [Fact]
    public void A_long_drop_off_a_building_is_not_a_flight()
    {
        // Free fall, which speeds up, and then keeps a steady speed once it stops speeding up.
        var verdict = Fly(new FlightCheck(Config()), at => 200f - (at < 5f ? 4.9f * at * at : 122.5f + 49f * (at - 5f)), 12f);

        Assert.Equal(FlightKind.None, verdict.Kind);
    }

    [Fact]
    public void A_fall_that_is_caught_partway_is_not_a_flight()
    {
        var verdict = Fly(new FlightCheck(Config()), at => at < 2f ? 100f - 4.9f * at * at : 80.4f, 5f);

        Assert.Equal(FlightKind.None, verdict.Kind);
    }

    [Fact]
    public void A_player_who_stands_still_is_not_a_flight()
    {
        Assert.Equal(FlightKind.None, Fly(new FlightCheck(Config()), _ => 12f, 10f).Kind);
    }

    [Fact]
    public void Each_player_is_judged_on_their_own_movement()
    {
        var check = new FlightCheck(Config());

        for (var step = 0; step <= 80; step++)
        {
            var at = Start.AddSeconds(step / 20f);
            check.Note(2, 5f, at);
            var flier = check.Note(1, step / 20f * 8f, at);

            if (flier.Kind != FlightKind.None)
            {
                Assert.Equal(FlightKind.None, check.Note(2, 5f, at).Kind);
                return;
            }
        }

        Assert.Fail("The climbing player was never called a flight");
    }

    [Fact]
    public void One_flight_is_called_once_and_not_on_every_pose()
    {
        var check = new FlightCheck(Config());
        var calls = 0;

        for (var step = 0; step <= 120; step++)
        {
            if (check.Note(1, step / 20f * 8f, Start.AddSeconds(step / 20f)).Kind != FlightKind.None)
            {
                calls++;
            }
        }

        // Six seconds of flying at a two second window, so a couple of calls, not a hundred and twenty.
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Zero_speed_turns_the_check_off()
    {
        var config = Config();
        config.FlightSpeed = 0f;

        Assert.Equal(FlightKind.None, Fly(new FlightCheck(config), at => at * 20f, 4f).Kind);
    }

    [Fact]
    public void A_player_who_left_is_forgotten()
    {
        var check = new FlightCheck(Config());
        check.Note(1, 0f, Start);
        check.Forget(1);

        Assert.Equal(FlightKind.None, check.Note(1, 40f, Start.AddSeconds(2)).Kind);
    }

    [Fact]
    public void Strikes_inside_the_window_reach_the_kick()
    {
        var config = Config();
        var check = new FlightCheck(config);

        for (var i = 1; i < config.FlightStrikesBeforeKick; i++)
        {
            Assert.False(check.Strike(1, Start.AddSeconds(i)));
        }

        Assert.True(check.Strike(1, Start.AddSeconds(config.FlightStrikesBeforeKick)));
    }

    [Fact]
    public void Strikes_that_age_out_do_not_count()
    {
        var config = Config();
        var check = new FlightCheck(config);
        var window = TimeSpan.FromSeconds(config.FlightStrikeWindowSeconds);

        for (var i = 0; i < config.FlightStrikesBeforeKick * 2; i++)
        {
            Assert.False(check.Strike(1, Start + window * (i + 1)));
        }
    }

    [Fact]
    public void Zero_strikes_never_kicks()
    {
        var config = Config();
        config.FlightStrikesBeforeKick = 0;
        var check = new FlightCheck(config);

        for (var i = 0; i < 10; i++)
        {
            Assert.False(check.Strike(1, Start.AddSeconds(i)));
        }
    }
}
