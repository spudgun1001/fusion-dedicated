namespace FusionDedicated.Server.Safety;

/// <summary>
/// Spawn rules ported from BoneLabAntiNuke, compiled in rather than read from a
/// file. They apply whenever extended protection is on, so protection cannot
/// disappear because blocklist.json was deleted or never seeded. A whitelist
/// entry in blocklist.json still permits any of these.
/// </summary>
public static class BuiltInSafety
{
    public static readonly IReadOnlySet<string> Barcodes = new HashSet<string>(
        new[]
        {
            "BaBaCorp.MiscExplosiveDevices.Spawnable.MicroNukeGrenade",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionMicroNuke",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionTimedNuke",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.M72LAW",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.M72LawINF",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.LAWRocket",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.KCB4VoidTunnelingDevice",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionVoidSuction",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.Missile",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.MiniMissile",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.IncinMissile",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionMissile",
            "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionFlameMissile",
            "SLZ.BONELAB.Core.Spawnable.GameplaySystems",
            "SLZ.BONELAB.Core.Spawnable.RigManagerBlank",
            "Rett64bit.DBDPack.Avatar.Pig",
            "cheetoboa.DL2improved.Avatar.DL2IMPDemolishermassive",
        },
        StringComparer.Ordinal);

    /// <summary>
    /// Matched anywhere in a barcode, case-insensitively, so a repackaged nuke
    /// nobody has listed is still caught.
    /// </summary>
    public static readonly IReadOnlyList<string> Keywords = new[]
    {
        "nuke",
        "nuclear",
        "tsar",
        "fatman",
        "littleboy",
        "atombomb",
        "warhead",
        "icbm",
        "blackhole",
        "singularity",
    };

    /// <summary>Used when blocklist.json is absent, so the guards still have limits.</summary>
    public const int DefaultMaxSpawnsPerSecond = 5;

    public const int DefaultMaxNicknameChangesPerMinute = 3;
}

/// <summary>Limits for the extended guards, whether or not blocklist.json exists.</summary>
public readonly record struct ExtendedLimits(
    int MaxSpawnsPerSecond,
    int MaxNicknameChangesPerMinute,
    IReadOnlyList<string> ReservedNicknames)
{
    public static ExtendedLimits Resolve(bool extendedProtection, BlocklistFile? file)
    {
        if (!extendedProtection)
        {
            return new ExtendedLimits(0, 0, Array.Empty<string>());
        }

        return new ExtendedLimits(
            file?.MaxSpawnsPerSecond ?? BuiltInSafety.DefaultMaxSpawnsPerSecond,
            file?.MaxNicknameChangesPerMinute ?? BuiltInSafety.DefaultMaxNicknameChangesPerMinute,
            file?.ReservedNicknames ?? (IReadOnlyList<string>)Array.Empty<string>());
    }
}
