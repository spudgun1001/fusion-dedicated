using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// BONELAB's damage figures are small: a bullet is around one and a glancing hit
/// a fraction of that. Rounded to whole numbers, every real hit read "for 0" and
/// the occasional one "for 1", so the combat log said nothing worth reading.
/// </summary>
public class CombatLogTests
{
    [Theory]
    [InlineData(0.45f, "for 0.45")]
    [InlineData(1.2f, "for 1.2")]
    [InlineData(0.07f, "for 0.07")]
    [InlineData(12f, "for 12")]
    [InlineData(37.5f, "for 37.5")]
    public void A_real_figure_is_readable(float damage, string expected)
        => Assert.Contains(expected, CombatLog.Describe("Kanzaaa", "Joel", damage));

    [Fact]
    public void A_whole_number_carries_no_trailing_zeros()
        => Assert.EndsWith("for 5", CombatLog.Describe("Kanzaaa", "Joel", 5f));

    [Fact]
    public void Both_names_are_in_it()
        => Assert.Equal("Kanzaaa hit Joel for 1.5", CombatLog.Describe("Kanzaaa", "Joel", 1.5f));

    [Fact]
    public void A_target_the_server_cannot_name_is_still_reported()
    {
        // The target is a small id, and they may have left by the time it lands.
        Assert.Contains("someone", CombatLog.Describe("Kanzaaa", null, 1.5f));
        Assert.Contains("someone", CombatLog.Describe("Kanzaaa", "  ", 1.5f));
    }

    [Fact]
    public void A_float_does_not_bring_its_whole_tail_along()
    {
        // The reason it was rounded to whole numbers in the first place. Two
        // places is enough to read and short enough for a log line.
        Assert.Equal("a hit b for 12.35", CombatLog.Describe("a", "b", 12.3456789f));
    }

    [Fact]
    public void Nothing_is_not_worth_a_line()
    {
        Assert.False(CombatLog.IsWorthLogging(0f));
        Assert.False(CombatLog.IsWorthLogging(-1f));
        Assert.True(CombatLog.IsWorthLogging(0.01f));
    }
}
