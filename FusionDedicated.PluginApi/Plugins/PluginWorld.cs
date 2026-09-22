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
    bool Persistent)
{
    /// <summary>Where a kept prop was kept, which its current position may have drifted from. Null for anything not kept.</summary>
    public (float X, float Y, float Z)? KeptAt { get; init; }
}

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

/// <summary>One of a player's body slots with something in it.</summary>
public readonly record struct PluginSlot(byte Index, ushort EntityId);

/// <summary>The vehicle seat a player sits in: the vehicle's entity id and the seat's index in it.</summary>
public readonly record struct PluginSeat(ushort EntityId, byte Index);

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

    /// <summary>How the server lists a player's filled body slots. Null outside a server.</summary>
    public Func<ulong, IReadOnlyList<PluginSlot>>? HolsteredLookup { get; set; }

    /// <summary>How the server lists what a player holds. Null outside a server.</summary>
    public Func<ulong, IReadOnlyList<ushort>>? HeldLookup { get; set; }

    /// <summary>How the server finds the seat a player sits in. Null outside a server.</summary>
    public Func<ulong, PluginSeat?>? SeatOfLookup { get; set; }

    /// <summary>How the server finds the entity a level object was networked as. Null outside a server.</summary>
    public Func<int, int, ushort?>? SceneEntityLookup { get; set; }

    public PluginEntity? Find(ushort entityId) => Lookup?.Invoke(entityId);

    public PluginMotion? Motion(ushort entityId) => MotionLookup?.Invoke(entityId);

    /// <summary>Who is holding an entity, empty when there is no server.</summary>
    public IReadOnlyList<ulong> Holders(ushort entityId) => HoldersLookup?.Invoke(entityId) ?? Array.Empty<ulong>();

    /// <summary>A player's filled body slots, empty when there is no server.</summary>
    public IReadOnlyList<PluginSlot> Holstered(ulong platformId)
        => HolsteredLookup?.Invoke(platformId) ?? Array.Empty<PluginSlot>();

    /// <summary>The entities in a player's hands, empty when there is no server.</summary>
    public IReadOnlyList<ushort> Held(ulong platformId)
        => HeldLookup?.Invoke(platformId) ?? Array.Empty<ushort>();

    /// <summary>The seat a player sits in, or null when they are standing or there is no server.</summary>
    public PluginSeat? SeatOf(ulong platformId) => SeatOfLookup?.Invoke(platformId);

    /// <summary>
    /// The entity a level object became when somebody grabbed it, by the hash and index Fusion names it with
    /// in the level. Null when nobody has networked it or there is no server.
    /// </summary>
    public ushort? SceneEntity(int hash, int index) => SceneEntityLookup?.Invoke(hash, index);

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

        // A kept prop is known by where it was kept, since poses can put it anywhere.
        var (x, y, z) = entity.KeptAt ?? (entity.X, entity.Y, entity.Z);

        return $"{Snap(x, grid)}:{Snap(y, grid)}:{Snap(z, grid)}";
    }

    private static long Snap(float value, float grid)
        => (long)Math.Round(value / grid, MidpointRounding.AwayFromZero);
}
