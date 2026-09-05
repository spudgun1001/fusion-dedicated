using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The server never sees a death. Health lives on the client being hit, so the
/// most it can honestly report is who hit whom and for how much.
/// </summary>
public class CombatLogTests
{
    [Fact]
    public void A_hit_names_both_players_and_the_damage()
    {
        string line = CombatLog.Describe("Badger", "crablet", 34.5f);

        Assert.Contains("Badger", line);
        Assert.Contains("crablet", line);
        Assert.Contains("35", line);
    }

    [Fact]
    public void An_unknown_target_is_said_rather_than_left_blank()
    {
        Assert.Contains("someone", CombatLog.Describe("Badger", null, 10f));
    }

    [Fact]
    public void Damage_is_rounded_rather_than_printed_to_seven_places()
    {
        Assert.DoesNotContain(".", CombatLog.Describe("a", "b", 12.3456789f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void A_hit_that_does_nothing_is_not_worth_a_line(float damage)
    {
        Assert.False(CombatLog.IsWorthLogging(damage));
    }

    [Fact]
    public void A_real_hit_is_worth_a_line()
    {
        Assert.True(CombatLog.IsWorthLogging(1f));
    }
}
