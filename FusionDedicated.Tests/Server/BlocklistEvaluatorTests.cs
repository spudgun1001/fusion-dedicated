using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class BlocklistEvaluatorTests
{
    private static BlocklistEvaluator Evaluator(params string[] operatorBarcodes)
        => new(new HashSet<string>(operatorBarcodes, StringComparer.Ordinal));

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
    public void An_unlisted_barcode_is_allowed_with_no_blocklist_file()
    {
        Assert.False(Evaluator().Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke").Blocked);
    }
}
