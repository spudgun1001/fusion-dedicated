using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A spawn request says what asked for it. A spawn menu reports source 2, while a
/// capture of an ordinary client spawn reported 1, so the source separates a menu
/// from whatever the game does for itself in a way the barcode cannot.
/// </summary>
public class SpawnSourceTests
{
    private static readonly string[] NoBarcodes = System.Array.Empty<string>();

    [Fact]
    public void An_exempt_source_passes_the_gate_whatever_the_rank()
    {
        var verdict = SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Any.Barcode", NoBarcodes, source: 1, exemptSources: new[] { 1 });

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void A_spawn_menu_is_still_held_to_the_rank()
    {
        var verdict = SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "CyberBlue.FodderFirearms.Spawnable.Sunders590", NoBarcodes,
            source: 2, exemptSources: new[] { 1 });

        Assert.True(verdict.Blocked);
    }

    [Fact]
    public void No_exempt_sources_means_the_rank_covers_everything()
    {
        Assert.True(SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Any.Barcode", NoBarcodes, source: 1, exemptSources: System.Array.Empty<int>()).Blocked);
    }

    [Fact]
    public void A_barcode_exemption_still_works_alongside_it()
    {
        Assert.False(SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Mod.Spawnable.RifleMagazine", new[] { "magazine" },
            source: 2, exemptSources: System.Array.Empty<int>()).Blocked);
    }

    [Fact]
    public void An_operator_is_unaffected_by_any_of_it()
    {
        Assert.False(SpawnAuthority.Check(
            PermissionLevel.Operator, PermissionLevel.Operator,
            "CyberBlue.FodderFirearms.Spawnable.Sunders590", NoBarcodes,
            source: 2, exemptSources: System.Array.Empty<int>()).Blocked);
    }
}
