using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The AntiNuke rules used to live only in blocklist.example.json, so deleting or
/// failing to seed that file silently removed every spawn rule. They are compiled
/// in now and keyed off extended protection instead.
/// </summary>
public class BuiltInSafetyTests
{
    private static BlocklistEvaluator Evaluator(bool extended, BlocklistFile? file = null)
        => new(
            new HashSet<string>(StringComparer.Ordinal),
            global: null,
            catalogue: null,
            file: file,
            extendedProtection: extended);

    [Fact]
    public void Built_in_list_is_not_empty()
    {
        Assert.NotEmpty(BuiltInSafety.Barcodes);
        Assert.NotEmpty(BuiltInSafety.Keywords);
    }

    [Theory]
    [InlineData("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke")]
    [InlineData("SLZ.BONELAB.Core.Spawnable.GameplaySystems")]
    public void Built_in_barcode_is_blocked_with_no_blocklist_file(string barcode)
    {
        var verdict = Evaluator(extended: true).Check(barcode);

        Assert.True(verdict.Blocked);
        Assert.Equal("built-in", verdict.Layer);
    }

    [Fact]
    public void Built_in_keyword_is_blocked_with_no_blocklist_file()
    {
        var verdict = Evaluator(extended: true).Check("Someone.Repack.Spawnable.BigNukeThing");

        Assert.True(verdict.Blocked);
        Assert.Equal("built-in", verdict.Layer);
    }

    [Fact]
    public void Built_in_list_is_off_when_extended_protection_is_off()
    {
        var verdict = Evaluator(extended: false)
            .Check("BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke");

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void Whitelist_overrides_a_built_in_barcode()
    {
        var file = new BlocklistFile
        {
            Whitelist = { "BaBaCorp.MiscExplosiveDevices.Spawnable.M72LAW" },
        };

        var verdict = Evaluator(extended: true, file)
            .Check("BaBaCorp.MiscExplosiveDevices.Spawnable.M72LAW");

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void Whitelist_overrides_a_built_in_keyword()
    {
        var file = new BlocklistFile { Whitelist = { "Mod.Pack.Spawnable.NukeCola" } };

        Assert.False(Evaluator(extended: true, file).Check("Mod.Pack.Spawnable.NukeCola").Blocked);
    }

    [Fact]
    public void Ordinary_barcode_is_still_allowed_with_the_built_in_list_on()
    {
        Assert.False(Evaluator(extended: true).Check("SLZ.BONELAB.Spawnable.Crate").Blocked);
    }

    [Fact]
    public void File_rules_still_apply_alongside_the_built_in_list()
    {
        var file = new BlocklistFile { Barcodes = { "Local.Mod.Spawnable.Banned" } };

        var verdict = Evaluator(extended: true, file).Check("Local.Mod.Spawnable.Banned");

        Assert.True(verdict.Blocked);
        Assert.Equal("blocklist", verdict.Layer);
    }
}
