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

    public DateTime SpawnedAt { get; } = DateTime.UtcNow;
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>
    /// True when the owner left and nobody has taken over. A dedicated server runs no
    /// physics, so an orphan simply hangs wherever it was — it needs adopting or culling.
    /// </summary>
    public bool IsOrphaned => OwnerSmallId == null;

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
/// server that never owns anything never simulates anything — but it must still know
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
    /// Clients permanently reserve entity ids 0-255 for player rigs — every client
    /// runs ReserveID over the whole player range at startup, and a player's rig is
    /// registered under their own small id. Allocating a prop below this therefore
    /// does not merely clash with another prop, it lands on top of a person, so a
    /// real host never hands out anything under 256.
    /// </summary>
    public const ushort FirstEntityId = 256;

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

    public TrackedEntity Register(ushort id, string barcode, byte owner, float x, float y, float z)
    {
        var entity = new TrackedEntity
        {
            Id = id,
            Barcode = barcode,
            OwnerSmallId = owner,
            X = x,
            Y = y,
            Z = z,
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

    public bool Remove(ushort id)
    {
        lock (_lock)
        {
            return _entities.Remove(id);
        }
    }

    /// <summary>
    /// Called when a player leaves. Their entities lose their simulator, so they are
    /// handed to whoever is named as heir — or left orphaned if the server is alone.
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
            foreach (var entity in _entities.Values.Where(e => e.IsOrphaned && e.LastUpdate < cutoff).ToList())
            {
                _entities.Remove(entity.Id);
                removed.Add(entity.Id);
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes props nobody is using: true orphans, and inherited props that have not
    /// moved for a while. Inherited ones matter because they are never ownerless, so
    /// orphan culling alone lets the world grow until it hits the cap.
    /// </summary>
    public List<ushort> CullStale(TimeSpan orphanTimeout, TimeSpan inheritedTimeout)
    {
        var removed = new List<ushort>();
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            foreach (var entity in _entities.Values.ToList())
            {
                bool stale = entity.IsOrphaned
                    ? now - entity.LastUpdate > orphanTimeout
                    : entity.Inherited && now - entity.LastUpdate > inheritedTimeout;

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
    public List<ushort> EvictOldest(int count)
    {
        var removed = new List<ushort>();

        lock (_lock)
        {
            var candidates = _entities.Values
                .Where(e => e.Inherited || e.IsOrphaned)
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

    public void Clear()
    {
        lock (_lock)
        {
            _entities.Clear();
        }
    }
}
