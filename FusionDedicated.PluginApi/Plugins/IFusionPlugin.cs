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

    void Despawn(ushort entityId);
}
