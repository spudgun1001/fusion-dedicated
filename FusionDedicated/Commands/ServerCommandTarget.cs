using FusionDedicated.Server;

namespace FusionDedicated.Commands;

/// <summary>Adapts FusionServer to what the command parser needs. Holds no logic.</summary>
public sealed class ServerCommandTarget : ICommandTarget
{
    private readonly FusionServer _server;
    private readonly Plugins.PluginHost? _plugins;

    public ServerCommandTarget(FusionServer server, Plugins.PluginHost? plugins = null)
    {
        _server = server;
        _plugins = plugins;
    }

    // Console and RCON run on their own threads, so every call into the world takes
    // the world lock. Listing and reloading plugins do not: a reload waits for
    // plugin timers.
    public IReadOnlyList<CommandPlayer> Players => _server.Exclusive(() => _server.Players.Players
        .Select(p => new CommandPlayer(
            p.PlatformId,
            p.SmallId,
            p.DisplayName,
            p.Permission,
            _server.Entities.Entities.Count(e => e.OwnerSmallId == p.SmallId)))
        .ToList());

    public void SetRank(ulong platformId, string name, PermissionLevel level)
        => _server.Exclusive(() => _server.SetPermission(platformId, name, level));

    public void Kick(byte smallId, string reason) => _server.Exclusive(() => _server.Kick(smallId, reason));

    public void Ban(ulong platformId, string name, string reason, TimeSpan? duration)
        => _server.Exclusive(() => _server.Ban(platformId, name, reason, duration, Server.Audit.AuditChannel.Console));

    public void Mute(ulong platformId, string name) => _server.Exclusive(() => _server.MutePlayer(platformId, name));

    public void Unmute(ulong platformId, string name) => _server.Exclusive(() => _server.UnmutePlayer(platformId, name));

    public bool Unban(ulong platformId) => _server.Exclusive(() => _server.Unban(platformId));

    public int Purge(byte smallId) => _server.Exclusive(() => _server.PurgeEntitiesOf(smallId));

    public void SetLevel(string barcode, string title)
        => _server.Exclusive(() => _server.SetLevel(barcode, title, -1, null));

    public IReadOnlyList<string> ListPlugins()
        => _plugins?.Loaded.Select(p => $"{p.Name} {p.Manifest.Version}").ToList()
           ?? (IReadOnlyList<string>)Array.Empty<string>();

    public int ReloadPlugins() => _plugins?.ReloadAll() ?? 0;
}
