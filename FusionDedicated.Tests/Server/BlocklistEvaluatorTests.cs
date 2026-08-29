using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class BlocklistEvaluatorTests
{
    private static BlocklistEvaluator Evaluator(params string[] operatorBarcodes)
        => new(new HashSet<string>(operatorBarcodes, StringComparer.Ordinal));

    [Fact]
    public void Built_in_barcode_is_blocked()
    {
        var verdict = Evaluator().Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke");

        Assert.True(verdict.Blocked);
        Assert.Equal("built-in", verdict.Layer);
    }

    [Fact]
    public void Operator_barcode_is_blocked()
    {
        var verdict = Evaluator("Some.Mod.Spawnable.Thing").Check("Some.Mod.Spawnable.Thing");

        Assert.True(verdict.Blocked);
        Assert.Equal("operator", verdict.Layer);
    }

    [Fact]
    public void Unlisted_barcode_is_allowed()
    {
        Assert.False(Evaluator().Check("SLZ.BONELAB.Spawnable.Crate").Blocked);
    }

    [Fact]
    public void Built_in_wins_over_an_operator_list_that_omits_it()
    {
        var verdict = Evaluator("Unrelated.Thing")
            .Check("BaBaCorp.MiscExplosiveDevices.Spawnable.MicroNukeGrenade");

        Assert.True(verdict.Blocked);
        Assert.Equal("built-in", verdict.Layer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_barcode_is_allowed_rather_than_throwing(string barcode)
    {
        Assert.False(Evaluator().Check(barcode).Blocked);
    }

    [Fact]
    public void Matching_is_case_sensitive_because_barcodes_are()
    {
        Assert.False(Evaluator().Check("babacorp.miscexplosivedevices.spawnable.timednuke").Blocked);
    }

    [Fact]
    public void Built_in_list_contains_the_known_crash_payload()
    {
        Assert.Contains("SLZ.BONELAB.Core.Spawnable.GameplaySystems", BuiltInBlocklist.Barcodes);
    }
}
