namespace FusionDedicated.Server.Safety;

/// <summary>
/// Works out whether a barcode is a magazine, a cartridge or a box of ammunition.
///
/// Reloading a gun asks the server for an entity the same way a spawn menu does,
/// so a rank gate on spawning stops people reloading unless ammunition is let
/// through. That was a config list, which meant a server with the list unedited
/// had guns nobody could reload, and it is the sort of thing an operator should
/// never have to think about. So it is built in and always on.
///
/// The Mag prefix is the modding convention: a magazine crate is named after the
/// gun it feeds, as Mag_AK47 or Magglock17gen5. Across 1005 such crates in a real
/// mod folder, six were something else, and those six are the exclusions below.
/// A magazine for one of them still passes, because its crate ends in mag.
/// </summary>
public static class Ammunition
{
    private static readonly string[] Words =
    {
        "magazine", "ammo", "ammunition", "cartridge", "speedloader",
        "quickload", "stripperclip", "shellbox", "roundbox", "bulletbox",
    };

    /// <summary>
    /// Crates that start with Mag and are a weapon rather than a magazine.
    ///
    /// Only these two. Adding the other Mag words a review suggested (magic,
    /// magma, magnet) looks safer and is not: MagMakarov and MagMA5C start with
    /// "magma", and excluding them stops people reloading. Checked against the
    /// 1005 crates beginning with Mag in a real mod folder, where the only
    /// weapons were three Magnums, a Magnum knife, MagnumKeyes and MagpulMasada.
    /// The rest of the collisions are props and cosmetics, and exempting a prop
    /// from a rank gate costs nothing.
    /// </summary>
    private static readonly string[] NotAmmo = { "magnum", "magpul" };

    /// <summary>
    /// The base game's own ammunition, by barcode.
    ///
    /// Base game crates predate the readable barcode format, so a magazine is
    /// something like c1534c5a-de30-4591-8dd2-53954d616761 with nothing in it to
    /// read. Every one of these was taken from the game's own pallet files under
    /// StreamingAssets/aa/StandaloneWindows64, so the list is the whole of it
    /// rather than the ones somebody happened to hit.
    ///
    /// The name is only here so the list can be checked by eye.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BaseGame =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["c1534c5a-e125-4c0e-aa56-ad9131324743"] = "12G Cartridge",
        ["c1534c5a-ee2b-464d-abab-648931324743"] = "12G Cartridge Case",
        ["c1534c5a-de30-4591-8dd2-53954d616761"] = "12G Shell Mag",
        ["c1534c5a-2d85-4fb0-a626-071d43617274"] = "12G Slug Cartridge",
        ["c1534c5a-662c-445a-b1fa-4ac843617274"] = "12G Slug Case Cartridge",
        ["c1534c5a-d7ea-4c98-a79d-244e4d616761"] = "12G Slug Mag",
        ["c1534c5a-53ea-4354-950c-166c4d616761"] = "12G Small Mag",
        ["c1534c5a-6125-45f0-ac59-a6954d616761"] = "1911 Mag",
        ["c1534c5a-89ec-414f-a99b-6b3043617274"] = "22 LR Cartridge",
        ["c1534c5a-d181-417c-b4d0-673d43617274"] = "22 LR Case Cartridge",
        ["c1534c5a-7dbf-4797-b9c5-1aa734304361"] = "40 Cartridge",
        ["c1534c5a-40c6-4d2d-b18b-589a34304361"] = "40 Cartridge Case",
        ["c1534c5a-43f1-4541-90f4-8ebd34354361"] = "45 Cartridge",
        ["c1534c5a-6f2b-4cd9-9d10-deb134354361"] = "45 Cartridge Case",
        ["c1534c5a-7538-4397-8ad7-a15a352e3536"] = "5.56 Cartridge",
        ["c1534c5a-b100-49d5-bc7f-fc4f352e3536"] = "5.56 Cartridge Case",
        ["c1534c5a-d440-472e-a58e-086d372e3632"] = "7.62 Cartridge",
        ["c1534c5a-255d-46f1-90af-fa63372e3632"] = "7.62 Cartridge Case",
        ["c1534c5a-7901-456e-8ed2-7d7f396d6d43"] = "9mm Cartridge",
        ["c1534c5a-0812-45fe-b074-0875396d6d43"] = "9mm Cartridge Case",
        ["c1534c5a-53c6-4aa3-8c88-93504d616761"] = "AKM Mag",
        ["c1534c5a-97a9-43f7-be30-6095416d6d6f"] = "Ammo Box Heavy",
        ["c1534c5a-683b-4c01-b378-6795416d6d6f"] = "Ammo Box Light",
        ["c1534c5a-57d4-4468-b5f0-c795416d6d6f"] = "Ammo Box Medium",
        ["SLZ.BONELAB.Content.Spawnable.DestAmmoBoxHeavyVariant"] = "Ammo Dest Box Heavy",
        ["SLZ.BONELAB.Content.Spawnable.DestAmmoBoxLightVariant"] = "Ammo Dest Box Light",
        ["SLZ.BONELAB.Content.Spawnable.DestAmmoBoxMediumVariant"] = "Ammo Dest Box Medium",
        ["SLZ.BONELAB.Content.Spawnable.BulletBanker"] = "Bullet Banker",
        ["SLZ.BONELAB.Content.Spawnable.CartridgePlasma"] = "Cartridge - Plasma",
        ["SLZ.BONELAB.Content.Spawnable.CartridgePlasmaCase"] = "Cartridge - Plasma Case",
        ["c1534c5a-e45e-4f53-a9ae-3c954d616761"] = "Eder Mag",
        ["c1534c5a-55c5-4e30-8ad4-a7074d61675f"] = "Gruber Mag",
        ["c1534c5a-9e31-482a-b426-43764d616761"] = "M1 Garand Mag",
        ["c1534c5a-8bb2-47cc-977a-46954d616761"] = "M16 Mag",
        ["c1534c5a-eae9-4837-9bf2-2fd94d616761"] = "M9 Mag",
        ["c1534c5a-233c-413a-b218-56954d616761"] = "MP5 Mag",
        ["SLZ.BONELAB.CORE.Spawnable.MagazineeMag"] = "Magazine - eMag",
        ["c1534c5a-dfeb-4562-9b6e-76d04d61675f"] = "P350 Mag",
        ["c1534c5a-ce15-4235-b0d6-7efd4d616761"] = "PDRC Mag",
        ["c1534c5a-9828-4ba4-8292-25734d616761"] = "PT8 Alaris Mag",
        ["c1534c5a-18ce-44aa-a416-63174d616761"] = "UMP Mag",
        ["c1534c5a-6e5b-4980-a3f2-95954d616761"] = "UZI Mag",
        ["c1534c5a-3030-4338-bf76-b67c4d616761"] = "Vector Mag",
    };

    public static bool IsAmmo(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        if (BaseGame.ContainsKey(barcode.Trim()))
        {
            return true;
        }

        // Only the crate name, so a pack called something like AmmoMods does not
        // exempt every gun in it.
        string crate = barcode.Contains('.')
            ? barcode[(barcode.LastIndexOf('.') + 1)..]
            : barcode;

        foreach (string word in Words)
        {
            if (crate.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!crate.StartsWith("mag", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string weapon in NotAmmo)
        {
            if (crate.StartsWith(weapon, StringComparison.OrdinalIgnoreCase)
                && !crate.EndsWith("mag", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
