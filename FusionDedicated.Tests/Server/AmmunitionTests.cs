using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class AmmunitionTests
{
    [Theory]
    // The one the log kept refusing. The base game predates readable barcodes, so
    // there is nothing in it to match on and it has to be known by name.
    [InlineData("c1534c5a-de30-4591-8dd2-53954d616761")]  // 12G Shell Mag
    [InlineData("c1534c5a-8bb2-47cc-977a-46954d616761")]  // M16 Mag
    [InlineData("c1534c5a-7901-456e-8ed2-7d7f396d6d43")]  // 9mm Cartridge
    [InlineData("c1534c5a-97a9-43f7-be30-6095416d6d6f")]  // Ammo Box Heavy
    [InlineData("SLZ.BONELAB.CORE.Spawnable.MagazineeMag")]
    [InlineData("SLZ.BONELAB.Content.Spawnable.BulletBanker")]
    public void Base_game_ammunition_is_ammunition(string barcode)
        => Assert.True(Ammunition.IsAmmo(barcode));

    [Fact]
    public void The_base_game_list_is_the_whole_of_it()
    {
        // Read out of the game's own pallets. If this number moves, the game was
        // updated and the list wants regenerating rather than patching.
        Assert.Equal(43, Ammunition.BaseGame.Count);
    }

    [Theory]
    [InlineData("c1534c5a-6b38-438a-a324-d7e147616467")]  // Nimbus gun
    [InlineData("c1534c5a-3813-49d6-a98c-f595436f6e73")]  // Constrainer
    public void A_base_game_dev_tool_is_not_ammunition(string barcode)
        => Assert.False(Ammunition.IsAmmo(barcode));

    [Theory]
    // The convention across a thousand mod crates: a magazine is named for the gun
    // it feeds.
    [InlineData("Rexmeck.GLOCK17.Spawnable.Magglock17gen5")]
    [InlineData("Remox.RemoxsWeaponPack.Spawnable.MagAK471")]
    [InlineData("Atlas.96.CounterStrike2PoolDay.Spawnable.MagGlockGeneric")]
    [InlineData("Astro.USPMatch.Spawnable.Magusphl2")]
    [InlineData("SomePack.Spawnable.MagMakarov")]
    [InlineData("SomePack.Spawnable.MagMA5C")]
    public void A_mod_magazine_is_ammunition(string barcode)
        => Assert.True(Ammunition.IsAmmo(barcode));

    [Theory]
    [InlineData("Anothuor.NMP.Spawnable.Blankcartridge")]
    [InlineData("Anothuor.NMP.Spawnable.AA12Cartridge")]
    [InlineData("Atlas.96.CounterStrike2PoolDay.Spawnable.AmmoCrate")]
    [InlineData("Some.Pack.Spawnable.SpeedloaderRevolver")]
    public void Cartridges_and_ammo_boxes_are_ammunition(string barcode)
        => Assert.True(Ammunition.IsAmmo(barcode));

    [Theory]
    // The six crates out of 1005 starting with Mag that are a weapon.
    [InlineData("SomePack.Spawnable.Magnum")]
    [InlineData("SomePack.Spawnable.MagnumKeyes")]
    [InlineData("SomePack.Spawnable.MagnumSurvivialKnife")]
    [InlineData("SomePack.Spawnable.MagpulMasada")]
    public void A_weapon_that_begins_with_mag_is_still_gated(string barcode)
        => Assert.False(Ammunition.IsAmmo(barcode));

    [Theory]
    [InlineData("SomePack.Spawnable.Magnummag")]
    [InlineData("SomePack.Spawnable.MagnumMag")]
    [InlineData("SomePack.Spawnable.MagnumAmmo")]
    public void A_magazine_for_one_of_those_weapons_still_passes(string barcode)
        => Assert.True(Ammunition.IsAmmo(barcode));

    [Theory]
    [InlineData("BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionGasGrenade")]
    [InlineData("SomePack.Spawnable.AK47")]
    [InlineData("SomePack.Spawnable.Nullbody")]
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_else_is_gated_as_before(string barcode)
        => Assert.False(Ammunition.IsAmmo(barcode));

    [Fact]
    public void Only_the_crate_name_is_read_not_the_pack()
    {
        // A pack called AmmoMods must not exempt every gun inside it.
        Assert.False(Ammunition.IsAmmo("Someone.AmmoMods.Spawnable.AssaultRifle"));
        Assert.True(Ammunition.IsAmmo("Someone.AmmoMods.Spawnable.MagAssaultRifle"));
    }

    [Fact]
    public void Ammunition_passes_the_rank_gate_with_nothing_configured()
    {
        // The point of building it in: an operator who never edited a list should
        // still have guns people can reload.
        var verdict = SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "c1534c5a-de30-4591-8dd2-53954d616761",
            Array.Empty<string>(), 2, Array.Empty<int>());

        Assert.False(verdict.Blocked);
    }

    [Fact]
    public void A_weapon_still_needs_the_rank_with_nothing_configured()
    {
        var verdict = SpawnAuthority.Check(
            PermissionLevel.Default, PermissionLevel.Operator,
            "SomePack.Spawnable.AK47",
            Array.Empty<string>(), 2, Array.Empty<int>());

        Assert.True(verdict.Blocked);
    }
}
