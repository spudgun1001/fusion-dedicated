namespace FusionDedicated.Server;

/// <summary>
/// Entities whose owner was asked for their state and has not answered yet. A game that has an
/// entity answers with its pose, so an entity nobody answers for is one the server lists but no
/// game has. Only touched from the main loop.
/// </summary>
public sealed class GhostProbe
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly Func<DateTime> _clock;
    private readonly Dictionary<ushort, Probe> _pending = new();

    public GhostProbe(Func<DateTime> clock)
    {
        _clock = clock;
    }

    public sealed class Probe
    {
        public required TrackedEntity Entity { get; init; }
        public required byte Requester { get; init; }
        public required byte Owner { get; init; }

        /// <summary>Who was asked once the owner stayed silent, or null while it is still the owner's turn.</summary>
        public byte? Second { get; set; }

        public DateTime Due { get; set; }
    }

    /// <summary>Starts a probe for this entity unless one is already running.</summary>
    public void Start(TrackedEntity entity, byte requester, byte owner)
        => _pending.TryAdd(entity.Id, new Probe { Entity = entity, Requester = requester, Owner = owner, Due = _clock() + Wait });

    public int Count => _pending.Count;

    /// <summary>A pose from either asked player shows they have the entity.</summary>
    public void Answered(ushort entityId, byte sender)
    {
        if (_pending.TryGetValue(entityId, out var probe)
            && (sender == probe.Owner || sender == probe.Second))
        {
            _pending.Remove(entityId);
        }
    }

    /// <summary>Takes out every probe past its deadline or whose asked player has gone.</summary>
    public List<Probe> TakeDue(Func<byte, bool> present)
    {
        if (_pending.Count == 0)
        {
            return new List<Probe>();
        }

        var now = _clock();
        var due = _pending.Values.Where(p => p.Due <= now || !present(p.Second ?? p.Owner)).ToList();

        foreach (var probe in due)
        {
            _pending.Remove(probe.Entity.Id);
        }

        return due;
    }

    /// <summary>Puts a probe back, now waiting on a second player.</summary>
    public void AskNext(Probe probe, byte next)
    {
        probe.Second = next;
        probe.Due = _clock() + Wait;
        _pending[probe.Entity.Id] = probe;
    }
}
