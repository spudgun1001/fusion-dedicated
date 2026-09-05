using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Server;

/// <summary>Who to move, and where to.</summary>
public readonly record struct TeleportPlan(byte MoveSmallId, Vec3 To);

/// <summary>
/// Works out which way round a teleport goes. Easy to get backwards, and getting it
/// backwards moves the wrong player.
/// </summary>
public static class TeleportPlanner
{
    public static TeleportPlan? For(
        ServerProtocol.PermissionCommand command,
        byte asker, Vec3 askerAt,
        byte other, Vec3 otherAt) => command switch
    {
        ServerProtocol.PermissionCommand.TeleportToThem => new TeleportPlan(asker, otherAt),
        ServerProtocol.PermissionCommand.TeleportToMe => new TeleportPlan(other, askerAt),
        _ => null,
    };
}
