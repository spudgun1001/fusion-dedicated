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
    private readonly Func<string, float, float, float, byte[], ushort>? _spawn;
    private readonly Func<ushort, string, bool>? _keep;
    private readonly Func<ushort, bool>? _forget;
    private readonly Func<ushort, ulong, bool>? _giveOwner;
    private readonly Func<string, float, float, float, byte[], ulong, ushort>? _spawnFor;
    private readonly Func<ushort, ulong, byte, bool>? _holster;
    private readonly Func<ulong, bool>? _unseat;

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

    public ServerPluginActions(Action<ulong, string> kick, Action<ulong, string> ban,
        Action<ulong, PermissionLevel> setRank, Action<ushort> despawn,
        Action<ulong, long, byte[]> sendModule, Action<long, byte[]> broadcastModule,
        Func<string, float, float, float, byte[], ushort> spawn,
        Func<ushort, string, bool> keep, Func<ushort, bool> forget)
        : this(kick, ban, setRank, despawn, sendModule, broadcastModule)
    {
        _spawn = spawn;
        _keep = keep;
        _forget = forget;
    }

    /// <summary>The one that also gives a plugin GiveOwner.</summary>
    public ServerPluginActions(Action<ulong, string> kick, Action<ulong, string> ban,
        Action<ulong, PermissionLevel> setRank, Action<ushort> despawn,
        Action<ulong, long, byte[]> sendModule, Action<long, byte[]> broadcastModule,
        Func<string, float, float, float, byte[], ushort> spawn,
        Func<ushort, string, bool> keep, Func<ushort, bool> forget,
        Func<ushort, ulong, bool> giveOwner)
        : this(kick, ban, setRank, despawn, sendModule, broadcastModule, spawn, keep, forget)
    {
        _giveOwner = giveOwner;
    }

    /// <summary>The one that also spawns for a chosen player and holsters.</summary>
    public ServerPluginActions(Action<ulong, string> kick, Action<ulong, string> ban,
        Action<ulong, PermissionLevel> setRank, Action<ushort> despawn,
        Action<ulong, long, byte[]> sendModule, Action<long, byte[]> broadcastModule,
        Func<string, float, float, float, byte[], ushort> spawn,
        Func<ushort, string, bool> keep, Func<ushort, bool> forget,
        Func<ushort, ulong, bool> giveOwner,
        Func<string, float, float, float, byte[], ulong, ushort> spawnFor,
        Func<ushort, ulong, byte, bool> holster)
        : this(kick, ban, setRank, despawn, sendModule, broadcastModule, spawn, keep, forget, giveOwner)
    {
        _spawnFor = spawnFor;
        _holster = holster;
    }

    /// <summary>The one that also stands seated players up.</summary>
    public ServerPluginActions(Action<ulong, string> kick, Action<ulong, string> ban,
        Action<ulong, PermissionLevel> setRank, Action<ushort> despawn,
        Action<ulong, long, byte[]> sendModule, Action<long, byte[]> broadcastModule,
        Func<string, float, float, float, byte[], ushort> spawn,
        Func<ushort, string, bool> keep, Func<ushort, bool> forget,
        Func<ushort, ulong, bool> giveOwner,
        Func<string, float, float, float, byte[], ulong, ushort> spawnFor,
        Func<ushort, ulong, byte, bool> holster,
        Func<ulong, bool> unseat)
        : this(kick, ban, setRank, despawn, sendModule, broadcastModule, spawn, keep, forget, giveOwner, spawnFor, holster)
    {
        _unseat = unseat;
    }

    public void Kick(ulong platformId, string reason) => _kick(platformId, reason);

    public void Ban(ulong platformId, string reason) => _ban(platformId, reason);

    public void SetRank(ulong platformId, PermissionLevel level) => _setRank(platformId, level);

    public void Despawn(ushort entityId) => _despawn(entityId);

    public void SendModule(ulong platformId, long handlerTag, byte[] payload)
        => _sendModule(platformId, handlerTag, payload);

    public void BroadcastModule(long handlerTag, byte[] payload)
        => _broadcastModule(handlerTag, payload);

    public ushort Spawn(string barcode, float x, float y, float z, byte[] rotation)
        => _spawn?.Invoke(barcode, x, y, z, rotation) ?? 0;

    public bool Keep(ushort entityId, string note) => _keep?.Invoke(entityId, note) ?? false;

    public bool Forget(ushort entityId) => _forget?.Invoke(entityId) ?? false;

    public bool GiveOwner(ushort entityId, ulong platformId) => _giveOwner?.Invoke(entityId, platformId) ?? false;

    public ushort SpawnFor(string barcode, float x, float y, float z, byte[] rotation, ulong ownerPlatformId)
        => _spawnFor?.Invoke(barcode, x, y, z, rotation, ownerPlatformId) ?? 0;

    public bool Holster(ushort entityId, ulong platformId, byte slotIndex)
        => _holster?.Invoke(entityId, platformId, slotIndex) ?? false;

    public bool Unseat(ulong platformId) => _unseat?.Invoke(platformId) ?? false;
}
