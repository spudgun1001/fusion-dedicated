using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The rank gate stops a spawn menu handing weapons to everyone, but a gun asking
/// for a magazine goes down the same path. Blocking that would stop people
/// reloading, so some barcodes pass the gate whatever the spawner's rank.
/// </summary>
public class SpawnExemptionTests
{
    private static readonly string[] Exempt = { "magazine", "ammo" };

    [Fact]
    public void A_magazine_passes_the_gate_for_anyone()
    {
        var verdict = SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Author.Pallet.Spawnable.AK74Magazine", Exempt);

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void Anything_else_is_still_held_to_the_rank()
    {
        var verdict = SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Author.Pallet.Spawnable.RocketLauncher", Exempt);

        Assert.True(verdict.Blocked);
    }

    [Fact]
    public void An_exact_barcode_can_be_exempted_since_base_game_ones_carry_no_name()
    {
        // Base game barcodes are opaque, so a keyword will never match one and the
        // operator has to name it outright.
        string[] exempt = { "c1534c5a-1234-4321-abcd-000000000000" };

        Assert.False(SpawnAuthority.Check(
            PermissionLevel.Guest, PermissionLevel.Owner,
            "c1534c5a-1234-4321-abcd-000000000000", exempt).Blocked);
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.False(SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Author.Pallet.Spawnable.SHOTGUN_MAGAZINE", Exempt).Blocked);
    }

    [Fact]
    public void An_empty_exemption_list_holds_everything_else_to_the_rank()
    {
        Assert.True(SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Author.Pallet.Spawnable.AK74", Array.Empty<string>()).Blocked);
    }

    [Fact]
    public void An_empty_exemption_list_still_lets_ammunition_through()
    {
        // This used to be blocked, and a server with the list unedited had guns
        // nobody could reload. Ammunition is built in now, so the config only ever
        // adds to it.
        Assert.False(SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Author.Pallet.Spawnable.AK74Magazine", Array.Empty<string>()).Blocked);
    }

    [Fact]
    public void A_blank_entry_never_matches_everything()
    {
        // A stray empty line in the config must not exempt the whole world.
        string[] exempt = { "", "   " };

        Assert.True(SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "Author.Pallet.Spawnable.RocketLauncher", exempt).Blocked);
    }

    [Fact]
    public void An_exemption_does_not_matter_when_the_gate_is_open_anyway()
    {
        Assert.False(SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Default,
            "Author.Pallet.Spawnable.RocketLauncher", Exempt).Blocked);
    }

    [Fact]
    public void The_default_list_covers_the_words_a_modded_magazine_uses()
    {
        foreach (string barcode in new[]
                 {
                     "Author.Pallet.Spawnable.M4Magazine",
                     "Author.Pallet.Spawnable.PistolAmmo",
                     "Author.Pallet.Spawnable.RifleCartridge",
                 })
        {
            Assert.False(SpawnAuthority.Check(
                PermissionLevel.Guest, PermissionLevel.Owner,
                barcode, ServerConfig.DefaultSpawningExempt).Blocked);
        }
    }
}
