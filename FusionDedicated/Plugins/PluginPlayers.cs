using FusionDedicated.Server;

namespace FusionDedicated.Plugins;

/// <summary>Turns a connected player into what a plugin is allowed to see of them.</summary>
public static class PluginPlayers
{
    public static PluginPlayer Snapshot(ConnectedPlayer player)
        => new(player.PlatformId, player.SmallId, player.DisplayName, player.Permission)
        {
            X = player.LastPosition.X,
            Y = player.LastPosition.Y,
            Z = player.LastPosition.Z,
            HasPosition = player.HasPosition,
        };
}
