namespace FusionDedicated.Web;

/// <summary>
/// One table saying what each endpoint needs, checked before any handler runs.
/// An endpoint nobody listed is refused, so adding a handler and forgetting to
/// place it locks it rather than opening it.
/// </summary>
public static class PanelPermissions
{
    private static readonly Dictionary<string, PanelRole> Required = new(StringComparer.Ordinal)
    {
        ["/"] = PanelRole.Viewer,
        ["/index.html"] = PanelRole.Viewer,

        ["/api/state"] = PanelRole.Viewer,
        ["/api/history"] = PanelRole.Viewer,
        ["/api/audit"] = PanelRole.Viewer,

        ["/api/kick"] = PanelRole.Moderator,
        ["/api/ban"] = PanelRole.Moderator,
        ["/api/bannote"] = PanelRole.Moderator,
        ["/api/unban"] = PanelRole.Moderator,
        ["/api/mute"] = PanelRole.Moderator,
        ["/api/purge"] = PanelRole.Moderator,
        ["/api/permission"] = PanelRole.Moderator,
        ["/api/level"] = PanelRole.Moderator,
        ["/api/levels"] = PanelRole.Moderator,
        ["/api/clear"] = PanelRole.Moderator,
        ["/api/persist"] = PanelRole.Moderator,
        ["/api/despawn"] = PanelRole.Moderator,
        ["/api/gather"] = PanelRole.Moderator,

        ["/api/settings"] = PanelRole.Owner,
        ["/api/restart"] = PanelRole.Owner,
        ["/api/accounts"] = PanelRole.Owner,

        // Anybody who can see the panel can see which plugins exist, but reading a
        // page or pressing one of its buttons is moderation.
        ["/api/modules"] = PanelRole.Owner,
        ["/api/plugins"] = PanelRole.Viewer,
        ["/api/plugins/page"] = PanelRole.Moderator,
        ["/api/plugins/action"] = PanelRole.Moderator,
    };

    /// <summary>
    /// Everything a banker may reach. Listed rather than implied by a rank,
    /// because a banker is not above or below anybody: they open the panel to
    /// move money and there is nothing else they are meant to be able to do.
    ///
    /// The page and the state behind it are here because the panel will not draw
    /// at all without them. What comes back on that state is cut down to almost
    /// nothing for a banker, so this is not a way in to the rest of it.
    /// </summary>
    private static readonly HashSet<string> BankerRoutes = new(StringComparer.Ordinal)
    {
        "/",
        "/index.html",
        "/api/state",
        "/api/plugins",
        "/api/plugins/page",
        "/api/plugins/action",
    };

    public static bool Allows(PanelRole role, string path)
    {
        string route = path.Split('?')[0];

        if (!Required.ContainsKey(route))
        {
            return false;
        }

        if (role == PanelRole.Banker)
        {
            return BankerRoutes.Contains(route);
        }

        return role >= Required[route];
    }

    /// <summary>
    /// Whether one role may see something marked for another.
    ///
    /// A banker sees only what is marked for a banker. Everybody else keeps the
    /// ordinary ladder, and a block put aside for bankers counts as moderation
    /// to them, so a moderator and an owner still see the whole page.
    /// </summary>
    public static bool CanSee(PanelRole actor, PanelRole required)
    {
        if (actor == PanelRole.Banker)
        {
            return required == PanelRole.Banker;
        }

        return actor >= (required == PanelRole.Banker ? PanelRole.Moderator : required);
    }

    /// <summary>
    /// Reads a role from the accounts file. Anything unrecognised becomes the
    /// least privilege rather than the most, since this is user-edited.
    /// </summary>
    public static PanelRole ParseRole(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "owner" => PanelRole.Owner,
            "moderator" => PanelRole.Moderator,
            "banker" => PanelRole.Banker,
            _ => PanelRole.Viewer,
        };
    }
}
