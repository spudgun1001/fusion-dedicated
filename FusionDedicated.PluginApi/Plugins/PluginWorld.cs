namespace FusionDedicated.Plugins;

/// <summary>One thing in the world, as a plugin sees it.</summary>
/// <param name="Persistent">
/// Marked to survive a restart, which is what the panel's Keep button does. A
/// kept prop comes back at the same place with a new entity id, so anything a
/// plugin wants to remember about it has to be held against where it is rather
/// than against the id.
/// </param>
public readonly record struct PluginEntity(
    ushort Id,
    string Barcode,
    ulong OwnerPlatformId,
    float X,
    float Y,
    float Z,
    bool Persistent);

/// <summary>
/// What is in the world, to read.
///
/// Reading only. A plugin that wants to remove something already has Despawn;
/// this answers the questions that need an entity looked up, chiefly where it is
/// and whether it has been kept.
/// </summary>
public sealed class PluginWorld
{
    /// <summary>
    /// How the server answers. Set by the host, and null when plugins are loaded
    /// outside a server, in which case nothing is found rather than throwing.
    /// </summary>
    public Func<ushort, PluginEntity?>? Lookup { get; set; }

    public PluginEntity? Find(ushort entityId) => Lookup?.Invoke(entityId);

    /// <summary>
    /// A place, rounded, as a key something can be remembered against.
    ///
    /// Rounded because a prop is put back where it was to within a float, and two
    /// props half a metre apart are two places. Fine enough to tell one phone on a
    /// wall from the next, coarse enough that nothing drifts off its own key.
    /// </summary>
    public static string PlaceOf(PluginEntity entity, float grid = 0.5f)
    {
        if (grid <= 0f)
        {
            grid = 0.5f;
        }

        return $"{Snap(entity.X, grid)}:{Snap(entity.Y, grid)}:{Snap(entity.Z, grid)}";
    }

    private static long Snap(float value, float grid)
        => (long)Math.Round(value / grid, MidpointRounding.AwayFromZero);
}
