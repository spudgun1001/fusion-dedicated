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
    private readonly Action<ulong, long, byte[]> _sendModule;
    private readonly Action<long, byte[]> _broadcastModule;

    public ServerPluginActions(Action<ulong, string> kick, Action<ulong, string> ban,
        Action<ulong, PermissionLevel> setRank, Action<ushort> despawn,
        Action<ulong, long, byte[]> sendModule, Action<long, byte[]> broadcastModule)
    {
        _kick = kick;
        _ban = ban;
        _setRank = setRank;
        _despawn = despawn;
        _sendModule = sendModule;
        _broadcastModule = broadcastModule;
    }

    public void Kick(ulong platformId, string reason) => _kick(platformId, reason);

    public void Ban(ulong platformId, string reason) => _ban(platformId, reason);

    public void SetRank(ulong platformId, PermissionLevel level) => _setRank(platformId, level);

    public void Despawn(ushort entityId) => _despawn(entityId);

    public void SendModule(ulong platformId, long handlerTag, byte[] payload)
        => _sendModule(platformId, handlerTag, payload);

    public void BroadcastModule(long handlerTag, byte[] payload)
        => _broadcastModule(handlerTag, payload);
}
