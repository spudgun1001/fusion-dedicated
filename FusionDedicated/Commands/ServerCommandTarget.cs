using FusionDedicated.Server;

namespace FusionDedicated.Commands;

/// <summary>Adapts FusionServer to what the command parser needs. Holds no logic.</summary>
public sealed class ServerCommandTarget : ICommandTarget
{
    private readonly FusionServer _server;

    public ServerCommandTarget(FusionServer server)
    {
        _server = server;
    }

    public IReadOnlyList<CommandPlayer> Players => _server.Players.Players
        .Select(p => new CommandPlayer(
            p.PlatformId,
            p.SmallId,
            p.DisplayName,
            p.Permission,
            _server.Entities.Entities.Count(e => e.OwnerSmallId == p.SmallId)))
        .ToList();

    public void SetRank(ulong platformId, string name, PermissionLevel level)
        => _server.SetPermission(platformId, name, level);

    public void Kick(byte smallId, string reason) => _server.Kick(smallId, reason);

    public void Ban(ulong platformId, string name, string reason, TimeSpan? duration)
        => _server.Ban(platformId, name, reason, duration, Server.Audit.AuditChannel.Console);

    public void Mute(ulong platformId, string name) => _server.MutePlayer(platformId, name);

    public void Unmute(ulong platformId, string name) => _server.UnmutePlayer(platformId, name);

    public bool Unban(ulong platformId) => _server.Unban(platformId);

    public int Purge(byte smallId) => _server.PurgeEntitiesOf(smallId);

    public void SetLevel(string barcode, string title)
        => _server.SetLevel(barcode, title, -1, null);
}
