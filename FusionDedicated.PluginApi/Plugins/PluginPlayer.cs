namespace FusionDedicated.Plugins;

/// <summary>
/// A connected player, as a plugin sees them. A snapshot rather than a live
/// object, so a plugin cannot reach into the server through it.
/// </summary>
public readonly record struct PluginPlayer(
    ulong PlatformId, byte SmallId, string Name, PermissionLevel Rank);
