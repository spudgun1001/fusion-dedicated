namespace FusionDedicated.Plugins;

/// <summary>Reading what an operator filled in on a plugin page.</summary>
public static class PluginValues
{
    /// <summary>Where the dashboard puts the signed-in account's name on every plugin action.</summary>
    public const string PanelUserKey = "panelUser";

    /// <summary>The panel account that pressed the button, or empty on a server too old to say.</summary>
    public static string PanelUser(IReadOnlyDictionary<string, string> values)
        => values.GetValueOrDefault(PanelUserKey, "");

    /// <summary>
    /// The player picked from the list, or the ID typed beside it.
    ///
    /// Both are offered together because the list only holds people who are
    /// connected, and an operator often wants to act on somebody who has just
    /// left. The picked one wins, since choosing a name is the deliberate act
    /// and a stale ID may be sitting in the box from last time.
    /// </summary>
    /// <returns>Null when neither was filled in, or what was there is not an ID.</returns>
    public static ulong? PlayerId(
        IReadOnlyDictionary<string, string> values,
        string pickedKey = "player",
        string typedKey = "steamId")
    {
        if (ulong.TryParse(values.GetValueOrDefault(pickedKey, "").Trim(), out ulong picked)
            && picked > 0)
        {
            return picked;
        }

        return ulong.TryParse(values.GetValueOrDefault(typedKey, "").Trim(), out ulong typed)
            && typed > 0
                ? typed
                : null;
    }
}
