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

/// <summary>How a thing is turned and moving, as of its latest pose.</summary>
/// <param name="Rotation">The seven bytes a spawn carries. Empty when nothing gave one.</param>
public readonly record struct PluginMotion(
    byte[] Rotation,
    float VelocityX,
    float VelocityY,
    float VelocityZ,
    DateTime SeenUtc)
{
    public float Speed => MathF.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY + VelocityZ * VelocityZ);
}

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

    /// <summary>
    /// How the server lists everything, for a plugin that has to find its own
    /// props rather than wait to be told about them.
    /// </summary>
    public Func<IReadOnlyList<PluginEntity>>? Everything { get; set; }

    /// <summary>How the server answers for rotation and velocity. Null outside a server.</summary>
    public Func<ushort, PluginMotion?>? MotionLookup { get; set; }

    /// <summary>How the server lists who is holding an entity. Null outside a server.</summary>
    public Func<ushort, IReadOnlyList<ulong>>? HoldersLookup { get; set; }

    public PluginEntity? Find(ushort entityId) => Lookup?.Invoke(entityId);

    public PluginMotion? Motion(ushort entityId) => MotionLookup?.Invoke(entityId);

    /// <summary>Who is holding an entity, empty when there is no server.</summary>
    public IReadOnlyList<ulong> Holders(ushort entityId) => HoldersLookup?.Invoke(entityId) ?? Array.Empty<ulong>();

    /// <summary>Everything in the world, empty when there is no server.</summary>
    public IReadOnlyList<PluginEntity> All()
        => Everything?.Invoke() ?? Array.Empty<PluginEntity>();

    /// <summary>
    /// Everything spawned from one crate.
    ///
    /// This is how a plugin finds its own props. A prop cannot announce itself:
    /// anything it fires as it comes into the world leaves before Fusion has
    /// given it a network entity, so it names no entity and cannot be replied to.
    /// </summary>
    public IReadOnlyList<PluginEntity> OfBarcode(string barcode)
        => barcode.Length == 0
            ? Array.Empty<PluginEntity>()
            : All().Where(e => string.Equals(e.Barcode, barcode, StringComparison.OrdinalIgnoreCase)).ToList();

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
