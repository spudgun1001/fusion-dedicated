namespace FusionDedicated.Web;

public enum PanelRole
{
    /// <summary>Sees everything, changes nothing.</summary>
    Viewer = 0,

    /// <summary>Deals with people and the world, but not the server itself.</summary>
    Moderator = 1,

    /// <summary>Everything, including settings, restarts and accounts.</summary>
    Owner = 2,
}

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
        ["/api/unban"] = PanelRole.Moderator,
        ["/api/mute"] = PanelRole.Moderator,
        ["/api/purge"] = PanelRole.Moderator,
        ["/api/permission"] = PanelRole.Moderator,
        ["/api/level"] = PanelRole.Moderator,
        ["/api/levels"] = PanelRole.Moderator,
        ["/api/clear"] = PanelRole.Moderator,

        ["/api/settings"] = PanelRole.Owner,
        ["/api/restart"] = PanelRole.Owner,
        ["/api/accounts"] = PanelRole.Owner,
    };

    public static bool Allows(PanelRole role, string path)
    {
        string route = path.Split('?')[0];

        return Required.TryGetValue(route, out var needed) && role >= needed;
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
            _ => PanelRole.Viewer,
        };
    }
}
