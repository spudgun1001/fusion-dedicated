using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Fusion works out whether you can be hurt as mortality AND NOT knockout. Turning
/// knockout on therefore sets every client invincible and leaves dying to a patch,
/// which reads as godmode when it does not fire. Neither setting says so in the
/// panel, so the server says it instead.
/// </summary>
public class MortalityCheckTests
{
    [Fact]
    public void Nothing_to_say_when_players_simply_die()
    {
        Assert.Null(MortalityCheck.WhyUnkillable(mortality: true, knockout: false));
    }

    [Fact]
    public void Mortality_off_is_reported()
    {
        string? why = MortalityCheck.WhyUnkillable(mortality: false, knockout: false);

        Assert.NotNull(why);
        Assert.Contains("mortality", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Knockout_on_is_reported_because_it_makes_clients_invincible()
    {
        string? why = MortalityCheck.WhyUnkillable(mortality: true, knockout: true);

        Assert.NotNull(why);
        Assert.Contains("knockout", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_knockout_warning_says_a_server_restart_will_not_fix_it()
    {
        // The flag that jams is a static in the player's own game, so the usual
        // reaction to godmode reports, restarting the server, achieves nothing.
        // Saying so is most of the value of this warning.
        string why = MortalityCheck.WhyUnkillable(mortality: true, knockout: true)!;

        Assert.Contains("restarting the server does nothing", why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restarting BONELAB", why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mortality_off_is_named_first_since_it_stops_everything()
    {
        string? why = MortalityCheck.WhyUnkillable(mortality: false, knockout: true);

        Assert.Contains("mortality", why!, StringComparison.OrdinalIgnoreCase);
    }
}
