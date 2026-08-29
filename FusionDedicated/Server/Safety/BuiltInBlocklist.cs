namespace FusionDedicated.Server.Safety;

/// <summary>
/// Grief payloads that no server should allow. Ported from BoneLabAntiNuke's
/// BarcodeMatcher.AlwaysBlocked. Configuration cannot whitelist these; adding one
/// means changing this file and shipping a build, which is deliberate.
/// </summary>
public static class BuiltInBlocklist
{
    public static readonly IReadOnlySet<string> Barcodes = new HashSet<string>(StringComparer.Ordinal)
    {
        // BaBaCorp Misc Explosive Devices (mod.io 4158753) — nukes
        "BaBaCorp.MiscExplosiveDevices.Spawnable.MicroNukeGrenade",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.TimedNuke",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionMicroNuke",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionTimedNuke",

        // M72 LAW launchers and their projectile
        "BaBaCorp.MiscExplosiveDevices.Spawnable.M72LAW",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.M72LawINF",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.LAWRocket",

        // Voidnade and its suction effect
        "BaBaCorp.MiscExplosiveDevices.Spawnable.KCB4VoidTunnelingDevice",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionVoidSuction",

        // Missiles and their explosion entities
        "BaBaCorp.MiscExplosiveDevices.Spawnable.Missile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.MiniMissile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.IncinMissile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionMissile",
        "BaBaCorp.MiscExplosiveDevices.Spawnable.ExplosionFlameMissile",

        // AIPClient crash payload
        "SLZ.BONELAB.Core.Spawnable.GameplaySystems",

        // Known griefer avatars
        "Rett64bit.DBDPack.Avatar.Pig",
        "cheetoboa.DL2improved.Avatar.DL2IMPDemolishermassive",
    };
}
