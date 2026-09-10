namespace FusionDedicated.Plugins;

/// <summary>
/// What a plugin has to provide. Shutdown is not optional: it is what lets the
/// server unload a plugin without restarting.
/// </summary>
public interface IFusionPlugin
{
    void Start(PluginContext context);

    void Shutdown();
}

/// <summary>
/// The things a plugin may do, as opposed to the things it may read. Kept apart so
/// a plugin that only watches is obvious from never touching this.
/// </summary>
public interface IPluginActions
{
    void Kick(ulong platformId, string reason);

    void Ban(ulong platformId, string reason);

    void SetRank(ulong platformId, PermissionLevel level);

    /// <summary>Removes a prop. A kept one stops being kept, so it does not come back after a restart.</summary>
    void Despawn(ushort entityId);

    /// <summary>
    /// Sends a module message to one player, stamped as coming from the server.
    /// Hosting a mod means sending messages nobody asked for, on other tags.
    /// </summary>
    void SendModule(ulong platformId, long handlerTag, byte[] payload);

    /// <summary>Sends a module message to everybody, stamped as from the server.</summary>
    void BroadcastModule(long handlerTag, byte[] payload);

    /// <summary>Puts a crate into the world and tells everybody. Returns the new entity id, or 0 when refused.</summary>
    ushort Spawn(string barcode, float x, float y, float z, byte[] rotation) => 0;

    /// <summary>Marks an entity to be put back after a restart, as the panel's Keep button does.</summary>
    bool Keep(ushort entityId, string note) => false;

    /// <summary>Stops putting an entity back after a restart.</summary>
    bool Forget(ushort entityId) => false;
}
