namespace FusionDedicated.Server;

/// <summary>
/// Fusion decides whether the local player can be hurt as mortality AND NOT
/// knockout, then leaves knockout itself to a patch on top of an invincible
/// player. So knockout being on is indistinguishable from godmode whenever that
/// patch does not run, and neither setting hints at it in the panel.
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
            return "Knockout is on. Fusion sets every client invincible and leaves "
                 + "dying to a patch, so a client where that patch does not run "
                 + "cannot be killed at all. Turn knockout off if players report godmode.";
        }

        return null;
    }
}
