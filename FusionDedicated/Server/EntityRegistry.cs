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

    /// <summary>How fast it was moving at its latest pose. Zero for a spawn nobody has moved.</summary>
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }

    /// <summary>
    /// How far the player who sent its latest pose was standing from it, or null
    /// when their position was not known. For the ammo cull's log line.
    /// </summary>
    public float? OwnerDistanceAtPose { get; set; }

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

    /// <summary>Where a kept prop's record puts it, so forgetting it finds that record after it drifts.</summary>
    public (float X, float Y, float Z)? KeptAt { get; set; }

    /// <summary>
    /// True when this is not a spawnable at all. The ends of a constraint are
    /// tracked so they count against the cap and can be culled, but their barcode
    /// is one we made up: asking a client to spawn it would be asking for
    /// something no pallet has.
    /// </summary>
    public bool Synthetic { get; set; }

    /// <summary>
    /// In a gun, or in a body slot, rather than lying loose.
    ///
    /// A magazine inside a gun is kinematic, so it sleeps, stops sending pose
    /// updates and from the outside is indistinguishable from one dropped on the
    /// floor an hour ago. Without this the ammo clock emptied holstered guns.
    /// </summary>
    public bool Attached { get; set; }

    /// <summary>
    /// True when the owner's own game has stopped simulating this, because it is
    /// too far away or in a zone they have left.
    ///
    /// Nothing moves it while that is true, so it hangs wherever it was. A client
    /// standing next to it takes it over and it falls, but only if it has been
    /// told the owner is not simulating it, and that is announced once when it
    /// happens. Anybody who joins afterwards never hears it.
    /// </summary>
    public bool CulledForOwner { get; set; }

    /// <summary>
    /// Fusion's EntitySource, as the spawn carried it. Repeated to a newcomer so
    /// their copy agrees with everybody else's about what the thing is.
    /// </summary>
    public byte Source { get; set; } = 2;

    /// <summary>
    /// The other end of the same constraint, when this is one. A delete names
    /// only one of the two, and clients drop both, so without this the other end
    /// stayed on our books for good and counted against the cap for ever.
    /// </summary>
    public ushort? Partner { get; set; }

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
    /// <summary>
    /// Things somebody spawned, which is what the world cap is about.
    ///
    /// A scene object is not clutter: it was in the level already and became
    /// networked because somebody touched it. Counting those against the cap let
    /// a busy level fill it with its own furniture and refuse every spawn, and
    /// nothing reclaims them, so it never recovered.
    /// </summary>
    public int SpawnedCount
    {
        get { lock (_lock) { return _entities.Values.Count(e => !e.Discovered); } }
    }

    public int DiscoveredCount
    {
        get { lock (_lock) { return _entities.Values.Count(e => e.Discovered); } }
    }

    public const ushort FirstEntityId = 256;

    /// <summary>
    /// A prop has left the books, however it left. Raised outside the lock, so a
    /// listener can read the registry without deadlocking it.
    /// </summary>
    public event Action<ushort>? Removed;

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

    /// <summary>Records that an entity's owner has stopped simulating it, or resumed.</summary>
    public void SetCulledForOwner(ushort id, bool culled)
    {
        lock (_lock)
        {
            if (_entities.TryGetValue(id, out var entity))
            {
                entity.CulledForOwner = culled;
            }
        }
    }

    /// <summary>Marks an entity as being in a gun or a slot, or no longer in one.</summary>
    public void SetAttached(ushort id, bool attached)
    {
        lock (_lock)
        {
            if (_entities.TryGetValue(id, out var entity))
            {
                entity.Attached = attached;
            }
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
    /// <param name="rotation">
    /// How it is turned now, in the seven byte form a spawn carries. Kept so a
    /// player joining later is told the object as it stands rather than as it was
    /// first spawned.
    /// </param>
    public void NotePose(ushort id, byte owner, float x, float y, float z,
        byte[]? rotation = null, float vx = 0f, float vy = 0f, float vz = 0f,
        float? ownerDistance = null)
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
                entity.VelocityX = vx;
                entity.VelocityY = vy;
                entity.VelocityZ = vz;

                if (rotation is { Length: > 0 })
                {
                    entity.Rotation = rotation;
                }

                entity.OwnerDistanceAtPose = ownerDistance;
                entity.LastUpdate = DateTime.UtcNow;
                return;
            }

            // A pose for an id nobody spawned is how scene props are noticed, and
            // it is also a free entity from an unauthenticated packet. Refusing
            // past the cap stops a client inflating the count, which is what made
            // the eviction below something a player could aim.
            if (_capacity > 0 && _entities.Count >= _capacity * 2)
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
                VelocityX = vx,
                VelocityY = vy,
                VelocityZ = vz,
                OwnerDistanceAtPose = ownerDistance,
            };
        }
    }

    public bool Remove(ushort id)
    {
        bool removed;

        lock (_lock)
        {
            removed = _entities.Remove(id);
        }

        if (removed)
        {
            Removed?.Invoke(id);
        }

        return removed;
    }

    private void Announce(List<ushort> removed)
    {
        foreach (ushort id in removed)
        {
            Removed?.Invoke(id);
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
                .Where(e => e.Removable && !e.Discovered && !e.Synthetic
                    && e.IsOrphaned && e.LastUpdate < cutoff)
                .ToList())
            {
                _entities.Remove(entity.Id);
                removed.Add(entity.Id);
            }
        }

        Announce(removed);

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
    /// <param name="ammoTimeout">
    /// A shorter clock for magazines, which are the bulk of what a busy server
    /// accumulates and the only props nobody misses. Applied to whichever branch
    /// the magazine falls in, so one dropped by somebody still here goes at the
    /// same age as one left behind. Zero leaves ammunition to the ordinary rules.
    /// </param>
    public List<ushort> CullStale(
        TimeSpan orphanTimeout, TimeSpan inheritedTimeout, TimeSpan idleTimeout = default,
        TimeSpan ammoTimeout = default)
        => CullStaleDetailed(orphanTimeout, inheritedTimeout, idleTimeout, ammoTimeout)
            .Select(e => e.Id)
            .ToList();

    /// <summary>
    /// The same cull, handing back the entities themselves so the log can say why
    /// each one went.
    /// </summary>
    public List<TrackedEntity> CullStaleDetailed(
        TimeSpan orphanTimeout, TimeSpan inheritedTimeout, TimeSpan idleTimeout = default,
        TimeSpan ammoTimeout = default)
    {
        var removed = new List<TrackedEntity>();
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            foreach (var entity in _entities.Values.ToList())
            {
                if (!entity.Removable)
                {
                    continue;
                }

                // A discovered entity is part of the level rather than a spawn.
                // Despawning one desynchronises everybody: with a poolee it
                // vanishes from the world, and without one the clients keep the
                // id while the server frees it to be handed out again.
                //
                // Only the idle branch used to say so. A vehicle or door somebody
                // networked and then left behind became inherited, and fifteen
                // minutes later it went.
                if (entity.Discovered)
                {
                    continue;
                }

                // A constraint end never moves, so it always looks idle. It goes
                // when a client deletes the constraint, or the level changes.
                if (entity.Synthetic)
                {
                    continue;
                }

                TimeSpan timeout;

                // Only the idle clock treats zero as "leave them alone". An
                // orphan or an inherited prop is culled on the number given,
                // whatever it is, which is how both have always behaved.
                bool zeroMeansNever = false;

                if (entity.IsOrphaned)
                {
                    timeout = orphanTimeout;
                }
                else if (entity.Inherited)
                {
                    timeout = inheritedTimeout;
                }
                else
                {
                    timeout = idleTimeout;
                    zeroMeansNever = true;
                }

                // Never longer than the branch would have allowed, so switching
                // this on can only ever remove a magazine sooner.
                // Attached is checked first and costs nothing: a magazine in a
                // gun, or a gun in a holster, is in use however long it has been
                // since it moved, so the shorter clock must never reach it.
                if (ammoTimeout > TimeSpan.Zero
                    && !entity.Attached
                    && (timeout <= TimeSpan.Zero || ammoTimeout < timeout)
                    && Safety.Ammunition.IsAmmo(entity.Barcode))
                {
                    timeout = ammoTimeout;
                    zeroMeansNever = false;
                }

                bool stale = (!zeroMeansNever || timeout > TimeSpan.Zero)
                    && now - entity.LastUpdate > timeout;

                if (stale)
                {
                    _entities.Remove(entity.Id);
                    removed.Add(entity);
                }
            }
        }

        Announce(removed.Select(e => e.Id).ToList());

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
                // Discovered ones came with the level and synthetic ones are not
                // spawnables, so despawning either tells clients about something
                // they cannot act on. That held on the last resort pass and not
                // on this one, so the same prop was safe from the timer and taken
                // in a burst at the cap instead.
                .Where(e => e.Removable
                    && !e.Discovered
                    && !e.Synthetic
                    // A magazine in a gun or a gun in a holster sleeps, so it
                    // looks idle while somebody is carrying it.
                    && (anyOwner
                        ? e.LastUpdate < cutoff && !e.Attached
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

        Announce(removed);

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
        List<ushort> removed;

        lock (_lock)
        {
            removed = _entities.Keys.ToList();
            _entities.Clear();
        }

        Announce(removed);

        return removed;
    }

    public List<ushort> Clear(bool includeDiscovered = false)
    {
        List<ushort> removed;

        lock (_lock)
        {
            // Surviving a clear is the point of marking something persistent, so
            // Clear all leaves them and removing one is its own deliberate press.
            removed = _entities.Values
                // A constraint end is left for its prop's clients to delete.
                .Where(e => e.Removable && !e.Synthetic && (includeDiscovered || !e.Discovered))
                .Select(e => e.Id)
                .ToList();

            foreach (ushort id in removed)
            {
                _entities.Remove(id);
            }
        }

        Announce(removed);

        return removed;
    }
}
