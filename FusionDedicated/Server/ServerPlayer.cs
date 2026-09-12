using FusionDedicated.Protocol;

namespace FusionDedicated.Server;

/// <summary>
/// The player 0 every client is told about. Fusion treats SmallID 0 as the host and
/// asks it about any entity with no owner, and a client that knew no player 0 threw
/// building every constraint it received.
///
/// Always loading and on no level, so no body is built for it and it never holds up
/// a level's all-players-loaded event.
/// </summary>
public static class ServerPlayer
{
    /// <summary>PolyBlank, which every install has, so no client tries to download it.</summary>
    public const string BlankAvatar = "c3534c5a-94b2-40a4-912a-24a8506f6c79";

    public static byte[] ConnectionResponse(ulong platformId, string serverName)
        => ServerProtocol.WriteConnectionResponse(
            platformId,
            PlayerRegistry.ServerSmallId,
            new Dictionary<string, string>
            {
                ["Username"] = serverName,
                ["Nickname"] = "",
                ["Loading"] = "True",
                ["LevelBarcode"] = "",
                ["PermissionLevel"] = PermissionLevel.Owner.ToFusionString(),
            },
            new List<string>(),
            BlankAvatar,
            Array.Empty<byte>(),
            isInitialJoin: false);
}
