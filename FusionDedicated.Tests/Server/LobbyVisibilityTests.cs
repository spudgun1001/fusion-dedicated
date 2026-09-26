using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The lobby dropped out of Fusion's browser while Steam still held it. The server
/// now looks for itself the way the browser does and republishes when it is missing.
/// </summary>
public class LobbyVisibilityTests
{
    private static readonly DateTime Start = new(2026, 9, 26, 18, 0, 0, DateTimeKind.Utc);

    private static LobbyVisibility Make() => new(TimeSpan.FromMinutes(5), Start);

    [Fact]
    public void Checks_every_five_minutes_by_default()
        => Assert.Equal(300, new ServerConfig().LobbyVisibilityCheckSeconds);

    [Fact]
    public void Checks_once_the_interval_has_passed()
    {
        var check = Make();

        Assert.False(check.Due(Start.AddMinutes(4)));
        Assert.True(check.Due(Start.AddMinutes(5)));
        Assert.False(check.Due(Start.AddMinutes(6)));
        Assert.True(check.Due(Start.AddMinutes(10)));
    }

    [Fact]
    public void Zero_interval_never_checks()
        => Assert.False(new LobbyVisibility(TimeSpan.Zero, Start).Due(Start.AddDays(1)));

    [Fact]
    public void Visible_lobby_is_left_alone()
    {
        var check = Make();

        Assert.False(check.ShouldRepublish(inBrowser: true, byCode: true, Start));
        Assert.False(check.ShouldRepublish(inBrowser: true, byCode: true, Start.AddMinutes(5)));
    }

    [Fact]
    public void Missing_from_the_browser_twice_in_a_row_republishes()
    {
        var check = Make();

        Assert.False(check.ShouldRepublish(inBrowser: false, byCode: true, Start));
        Assert.True(check.ShouldRepublish(inBrowser: false, byCode: true, Start.AddMinutes(5)));
    }

    [Fact]
    public void A_visible_check_in_between_starts_the_count_again()
    {
        var check = Make();

        Assert.False(check.ShouldRepublish(inBrowser: false, byCode: true, Start));
        Assert.False(check.ShouldRepublish(inBrowser: true, byCode: true, Start.AddMinutes(5)));
        Assert.False(check.ShouldRepublish(inBrowser: false, byCode: true, Start.AddMinutes(10)));
    }

    [Fact]
    public void Missing_by_code_republishes_at_once()
        => Assert.True(Make().ShouldRepublish(inBrowser: false, byCode: false, Start));

    [Fact]
    public void Browser_republish_happens_at_most_once_per_15_minutes()
    {
        var check = Make();

        check.ShouldRepublish(false, true, Start);
        Assert.True(check.ShouldRepublish(false, true, Start.AddMinutes(5)));

        Assert.False(check.ShouldRepublish(false, true, Start.AddMinutes(10)));
        Assert.False(check.ShouldRepublish(false, true, Start.AddMinutes(15)));
        Assert.True(check.ShouldRepublish(false, true, Start.AddMinutes(20)));
    }

    [Fact]
    public void Missing_by_code_is_not_rate_limited()
    {
        var check = Make();

        Assert.True(check.ShouldRepublish(false, false, Start));
        Assert.True(check.ShouldRepublish(false, false, Start.AddMinutes(5)));
    }
}
