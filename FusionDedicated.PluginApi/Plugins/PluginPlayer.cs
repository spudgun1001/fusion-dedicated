namespace FusionDedicated.Plugins;

/// <summary>
/// A connected player, as a plugin sees them. A snapshot rather than a live
/// object, so a plugin cannot reach into the server through it.
/// </summary>
public readonly record struct PluginPlayer(
    ulong PlatformId, byte SmallId, string Name, PermissionLevel Rank)
{
    // Kept out of the constructor so plugins built against the older shape still load.

    /// <summary>Where their pelvis was last seen, from the pose their game sends.</summary>
    public float X { get; init; }

    public float Y { get; init; }

    public float Z { get; init; }

    /// <summary>False until their first pose arrives, when the position is just zero.</summary>
    public bool HasPosition { get; init; }
}
