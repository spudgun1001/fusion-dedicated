namespace FusionDedicated.Server;

public sealed class TrackedEntity
{
    public required ushort Id { get; init; }
    public required string Barcode { get; init; }

    public byte? OwnerSmallId { get; set; }

    /// <summary>
    /// True once this was handed to somebody because its previous owner left. The
    /// current owner did not ask for it, so it must not count against their limits.
    /// </summary>
    public bool Inherited { get; set; }

    /// <summary>
    /// Learned from a pose update rather than a spawn request, so its barcode is
    /// unknown and it may be a scene prop somebody picked up rather than a spawn.
    /// </summary>
    public bool Discovered { get; set; }

    public DateTime SpawnedAt { get; } = DateTime.UtcNow;
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>
    /// The seven rotation bytes this was spawned with, kept so a prop can be put
    /// back the way round it was. Empty for anything the server only learned about
    /// from a pose update.
    /// </summary>
    public byte[] Rotation { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Placed on purpose and meant to stay. Exempt from every cull, from eviction
    /// at the entity cap, and from Clear all.
    /// </summary>
    public bool Persistent { get; set; }

    /// <summary>
    /// True when this is not a spawnable at all. The ends of a constraint are
    /// tracked so they count against the cap and can be culled, but their barcode
    /// is one we made up: asking a client to spawn it would be asking for
    /// something no pallet has.
    /// </summary>
    public bool Synthetic { get; set; }

    /// <summary>
    /// True when the owner left and nobody has taken over. A dedicated server runs no
    /// physics, so an orphan simply hangs wherever it was, it needs adopting or culling.
    /// </summary>
    public bool IsOrphaned => OwnerSmallId == null;

    /// <summary>
    /// A persistent prop is deliberately ownerless, so nobody simulates it and it
    /// stays where it was put. That would normally make it an orphan to be culled,
    /// which is why every cull asks this first.
    /// </summary>
    public bool Removable => !Persistent;

    public string ShortName
    {
        get
        {
            var parts = Barcode.Split('.');
            return parts.Length > 0 ? parts[^1] : Barcode;
        }
    }
}

/// <summary>
/// Allocates entity IDs and remembers what exists in the world.
///
/// This is the piece that replaces physics on a dedicated server. Fusion gives each
/// entity an owner, and only that owner simulates it; the host merely relays. So a
/// server that never owns anything never simulates anything, but it must still know
/// what exists, in order to catch late joiners up.
/// </summary>
public sealed class EntityRegistry
{
    private readonly Dictionary<ushort, TrackedEntity> _entities = new();
    private readonly object _lock = new();

    private ushort _nextId = FirstEntityId;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _entities.Count;
            }
        }
    }

    public int OrphanCount
    {
        get
        {
            lock (_lock)
            {
                return _entities.Values.Count(e => e.IsOrphaned);
            }
        }
    }

    public IReadOnlyList<TrackedEntity> Entities
    {
        get
        {
            lock (_lock)
            {
                return _entities.Values.OrderBy(e => e.Id).ToList();
            }
        }
    }

    /// <summary>
    /// First id a prop may take.
    ///
    /// Clients permanently reserve entity ids 0-255 for player rigs, every client
    /// runs ReserveID over the whole player range at startup, and a player's rig is
    /// registered under their own small id. Allocating a prop below this therefore
    /// does not merely clash with another prop, it lands on top of a person, so a
    /// real host never hands out anything under 256.
    /// </summary>
    public int DiscoveredCount
    {
        get { lock (_lock) { return _entities.Values.Count(e => e.Discovered); } }
    }

    public const ushort FirstEntityId = 256;

    /// <summary>
    /// The most entities to hold, or zero for no limit. Only consulted where an
    /// entity would be created from something a client sent unprompted.
    /// </summary>
    public int Capacity
    {
        get => _capacity;
        set => _capacity = value;
    }

    private int _capacity;

    /// <summary>Next id the allocator will try. Exposed so pressure is visible.</summary>
    public ushort NextId
    {
        get
        {
            lock (_lock)
            {
                return _nextId;
            }
        }
    }

    public ushort AllocateId()
    {
        lock (_lock)
        {
            if (_nextId < FirstEntityId)
            {
                _nextId = FirstEntityId;
            }

            while (_entities.ContainsKey(_nextId))
            {
                // Ids are a ushort on the wire, so wrap back to the first prop id
                // rather than overflowing into the player range.
                _nextId = _nextId >= ushort.MaxValue ? FirstEntityId : (ushort)(_nextId + 1);
            }

            ushort allocated = _nextId;

            _nextId = _nextId >= ushort.MaxValue ? FirstEntityId : (ushort)(_nextId + 1);

            return allocated;
        }
    }

    public TrackedEntity Register(ushort id, string barcode, byte owner, float x, float y, float z,
        byte[]? rotation = null)
    {
        var entity = new TrackedEntity
        {
            Id = id,
            Barcode = barcode,
            OwnerSmallId = owner,
            X = x,
            Y = y,
            Z = z,
            Rotation = rotation ?? Array.Empty<byte>(),
        };

        lock (_lock)
        {
            _entities[id] = entity;
        }

        return entity;
    }

    public TrackedEntity? Get(ushort id)
    {
        lock (_lock)
        {
            return _entities.GetValueOrDefault(id);
        }
    }

    public void SetOwner(ushort id, byte? owner)
    {
        lock (_lock)
        {
            if (_entities.TryGetValue(id, out var entity))
            {
                entity.OwnerSmallId = owner;
                entity.LastUpdate = DateTime.UtcNow;
            }
        }
    }

    public void UpdatePosition(ushort id, float x, float y, float z)
    {
        lock (_lock)
        {
            if (_entities.TryGetValue(id, out var entity))
            {
                entity.X = x;
                entity.Y = y;
                entity.Z = z;
                entity.LastUpdate = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Records a pose, registering the entity if this is the first we have heard of
    /// it. Ids below FirstEntityId are player rigs and are left alone.
    /// </summary>
    public void NotePose(ushort id, byte owner, float x, float y, float z)
    {
        if (id < FirstEntityId)
        {
            return;
        }

        lock (_lock)
        {
            if (_entities.TryGetValue(id, out var entity))
            {
                entity.X = x;
                entity.Y = y;
                entity.Z = z;
                entity.LastUpdate = DateTime.UtcNow;
                return;
            }

            // A pose for an id nobody spawned is how scene props are noticed, and
            // it is also a free entity from an unauthenticated packet. Refusing
            // past the cap stops a client inflating the count, which is what made
            // the eviction below something a player could aim.
            if (_capacity > 0 && _entities.Count >= _capacity)
            {
                return;
            }

            _entities[id] = new TrackedEntity
            {
                Id = id,
                Barcode = "",
                OwnerSmallId = owner,
                Discovered = true,
                X = x,
                Y = y,
                Z = z,
            };
        }
    }

    public bool Remove(ushort id)
    {
        lock (_lock)
        {
            return _entities.Remove(id);
        }
    }

    /// <summary>
    /// Called when a player leaves. Their entities lose their simulator, so they are
    /// handed to whoever is named as heir, or left orphaned if the server is alone.
    /// </summary>
    public List<TrackedEntity> Orphan(byte departedSmallId, byte? heir)
    {
        var affected = new List<TrackedEntity>();

        lock (_lock)
        {
            foreach (var entity in _entities.Values.Where(e => e.OwnerSmallId == departedSmallId))
            {
                entity.OwnerSmallId = heir;
                entity.Inherited = heir.HasValue;
                entity.LastUpdate = DateTime.UtcNow;
                affected.Add(entity);
            }
        }

        return affected;
    }

    /// <summary>
    /// Drops orphans that nobody has claimed for a while. Without this they accumulate
    /// forever, frozen in mid-air, because no one is left to apply gravity to them.
    /// </summary>
    public List<ushort> CullOrphans(TimeSpan olderThan)
    {
        var removed = new List<ushort>();
        var cutoff = DateTime.UtcNow - olderThan;

        lock (_lock)
        {
            foreach (var entity in _entities.Values
                .Where(e => e.Removable && !e.Discovered && e.IsOrphaned && e.LastUpdate < cutoff)
                .ToList())
            {
                _entities.Remove(entity.Id);
                removed.Add(entity.Id);
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes props nobody is using: true orphans, inherited props that have not
    /// moved for a while, and, when an idle timeout is given, props whose owner is
    /// still connected but has not touched them since. Inherited ones matter because
    /// they are never ownerless, so orphan culling alone lets the world grow until it
    /// hits the cap. The idle clock matters because a magazine dropped by somebody
    /// still playing is neither orphaned nor inherited, so nothing else removes it.
    /// </summary>
    /// <param name="idleTimeout">Zero to leave a connected player's props alone.</param>
    public List<ushort> CullStale(
        TimeSpan orphanTimeout, TimeSpan inheritedTimeout, TimeSpan idleTimeout = default)
    {
        var removed = new List<ushort>();
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            foreach (var entity in _entities.Values.ToList())
            {
                if (!entity.Removable)
                {
                    continue;
                }

                bool stale;

                if (entity.IsOrphaned)
                {
                    stale = now - entity.LastUpdate > orphanTimeout;
                }
                else if (entity.Inherited)
                {
                    stale = now - entity.LastUpdate > inheritedTimeout;
                }
                else
                {
                    // A discovered entity may be part of the level rather than a
                    // spawn, and despawning one of those desynchronises everybody.
                    stale = idleTimeout > TimeSpan.Zero
                        && !entity.Discovered
                        && now - entity.LastUpdate > idleTimeout;
                }

                if (stale)
                {
                    _entities.Remove(entity.Id);
                    removed.Add(entity.Id);
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// Frees space at the cap by dropping the least recently touched abandoned props.
    /// Only inherited or ownerless ones are eligible, so a player's own work is never
    /// taken away to make room for someone else.
    /// </summary>
    /// <param name="anyOwner">
    /// A last resort. When every entity belongs to somebody still connected there is
    /// nothing abandoned to take, and a full world stays full: from then on every
    /// spawn is refused, for everybody, until a restart. A busy server reached that
    /// after three hours and nobody could spawn anything. Losing the least recently
    /// touched prop is worse for one player than the world being locked is for all
    /// of them, so this widens the search rather than refusing.
    /// </param>
    /// <param name="idleFor">
    /// With <paramref name="anyOwner"/>, how long an entity must have sat still
    /// before it can be taken. Without it a player's own prop is destroyed while
    /// they are holding it, and worse, the order is by last update, so anybody
    /// who can add entities faster than they are evicted decides whose work
    /// goes. Requiring real idleness means nothing in use is ever a candidate.
    /// </param>
    public List<ushort> EvictOldest(int count, bool anyOwner = false, TimeSpan idleFor = default)
    {
        var removed = new List<ushort>();
        var cutoff = DateTime.UtcNow - (idleFor == default ? TimeSpan.FromMinutes(2) : idleFor);

        lock (_lock)
        {
            var candidates = _entities.Values
                .Where(e => e.Removable
                    && (anyOwner
                        // Discovered ones came with the level and synthetic ones
                        // are not spawnables, so despawning either tells clients
                        // about something they cannot act on.
                        ? !e.Discovered && !e.Synthetic && e.LastUpdate < cutoff
                        : e.Inherited || e.IsOrphaned))
                .OrderBy(e => e.LastUpdate)
                .Take(count)
                .ToList();

            foreach (var entity in candidates)
            {
                _entities.Remove(entity.Id);
                removed.Add(entity.Id);
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes everything and reports what went. Discovered entities are held back
    /// unless asked for: one may be a scene prop, and despawning that desynchronises
    /// every client.
    /// </summary>
    /// <summary>
    /// Empties the books completely, persistent props included. For a level change,
    /// where the old world is gone whatever it was made of. The props themselves are
    /// kept in their own file and go back on the level they belong to.
    /// </summary>
    public List<ushort> Forget()
    {
        lock (_lock)
        {
            var removed = _entities.Keys.ToList();
            _entities.Clear();

            return removed;
        }
    }

    public List<ushort> Clear(bool includeDiscovered = false)
    {
        lock (_lock)
        {
            // Surviving a clear is the point of marking something persistent, so
            // Clear all leaves them and removing one is its own deliberate press.
            var removed = _entities.Values
                .Where(e => e.Removable && (includeDiscovered || !e.Discovered))
                .Select(e => e.Id)
                .ToList();

            foreach (ushort id in removed)
            {
                _entities.Remove(id);
            }

            return removed;
        }
    }
}
