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
    public void Mortality_off_is_named_first_since_it_stops_everything()
    {
        string? why = MortalityCheck.WhyUnkillable(mortality: false, knockout: true);

        Assert.Contains("mortality", why!, StringComparison.OrdinalIgnoreCase);
    }
}
