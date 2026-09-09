namespace FusionDedicated.Server;

/// <summary>
/// Fusion decides whether the local player can be hurt as mortality AND NOT
/// knockout, then leaves knockout itself to a patch on top of an invincible
/// player. So knockout being on is indistinguishable from godmode whenever that
/// patch does not run, and neither setting hints at it in the panel.
///
/// The patch stops running for a reason worth naming, because the usual reaction
/// to godmode is to restart the server and that never helps. Fusion's
/// LocalRagdoll holds one static flag while a knockout is in progress, and clears
/// it only where the timer runs out. A knockout cut short by a level load leaves
/// the flag set, and from then on every knockout is refused on that client while
/// its player stays invincible.
/// </summary>
public static class MortalityCheck
{
    /// <summary>Why nobody can be killed, or null when they can.</summary>
    public static string? WhyUnkillable(bool mortality, bool knockout)
    {
        if (!mortality)
        {
            return "Mortality is off, so nobody can be hurt.";
        }

        if (knockout)
        {
            return "Knockout is on, so Fusion makes every player invincible and "
                 + "leaves going down to a knockout timer. A knockout cut short by "
                 + "a level load leaves that client stuck: it refuses every "
                 + "knockout afterwards and the player cannot be brought down at "
                 + "all. It is held in their game, not here, so restarting the "
                 + "server does nothing and only that player restarting BONELAB "
                 + "clears it. Turn knockout off to have players die normally.";
        }

        return null;
    }
}
