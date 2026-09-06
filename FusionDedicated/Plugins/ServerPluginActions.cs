namespace FusionDedicated.Plugins;

/// <summary>
/// The plugin actions, pointed at the server. Delegates rather than a reference to
/// FusionServer, so the whole set of things a plugin can do is visible in one
/// place and testable without a server.
/// </summary>
public sealed class ServerPluginActions : IPluginActions
{
    private readonly Action<ulong, string> _kick;
    private readonly Action<ulong, string> _ban;
    private readonly Action<ulong, PermissionLevel> _setRank;
    private readonly Action<ushort> _despawn;

    public ServerPluginActions(Action<ulong, string> kick, Action<ulong, string> ban,
        Action<ulong, PermissionLevel> setRank, Action<ushort> despawn)
    {
        _kick = kick;
        _ban = ban;
        _setRank = setRank;
        _despawn = despawn;
    }

    public void Kick(ulong platformId, string reason) => _kick(platformId, reason);

    public void Ban(ulong platformId, string reason) => _ban(platformId, reason);

    public void SetRank(ulong platformId, PermissionLevel level) => _setRank(platformId, level);

    public void Despawn(ushort entityId) => _despawn(entityId);
}
