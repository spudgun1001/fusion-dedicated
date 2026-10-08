namespace FusionDedicated.Server;

/// <summary>
/// Entities a player was asked about and has not answered for yet. A game that has an entity
/// answers with its pose, so an entity nobody answers for is one the server lists but no game has.
/// </summary>
public sealed class GhostProbe
{
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly Func<DateTime> _clock;
    private readonly Dictionary<ushort, Probe> _pending = new();
    private DateTime _nextDue = DateTime.MaxValue;

    /// <summary>A departure can arrive off the main loop, as a kick.</summary>
    private readonly object _lock = new();

    public GhostProbe(Func<DateTime> clock)
    {
        _clock = clock;
    }

    public sealed class Probe
    {
        public required TrackedEntity Entity { get; init; }
        public required byte Requester { get; init; }

        /// <summary>Who was asked first: the owner, or the steadiest other player whose game built it.</summary>
        public required byte Asked { get; init; }

        /// <summary>The owner when it started, so a probe ends if the entity changes hands.</summary>
        public byte? Owner { get; init; }

        public required string OwnerName { get; init; }

        /// <summary>Who was asked once the first stayed silent, or null while it is still their turn.</summary>
        public byte? Second { get; set; }

        public DateTime Due { get; set; }
    }

    public int Count => _pending.Count;

    /// <summary>Starts a probe for this entity, unless one is already running.</summary>
    public bool Start(TrackedEntity entity, byte requester, byte asked, string ownerName)
    {
        lock (_lock)
        {
            var probe = new Probe
            {
                Entity = entity, Requester = requester, Asked = asked, Owner = entity.OwnerSmallId,
                OwnerName = ownerName, Due = _clock() + Wait,
            };

            if (!_pending.TryAdd(entity.Id, probe))
            {
                return false;
            }

            Schedule(probe.Due);
            return true;
        }
    }

    /// <summary>A pose from a player who was asked shows they have it. True when that ended a probe.</summary>
    public bool Answered(ushort entityId, byte sender)
    {
        lock (_lock)
        {
            return _pending.TryGetValue(entityId, out var probe)
                   && (sender == probe.Asked || sender == probe.Second)
                   && _pending.Remove(entityId);
        }
    }

    /// <summary>Ends what a leaver asked for, and stops waiting on any answer from them.</summary>
    public void Depart(byte player)
    {
        lock (_lock)
        {
            foreach (var probe in _pending.Values.ToList())
            {
                if (probe.Requester == player)
                {
                    _pending.Remove(probe.Entity.Id);
                }
                else if ((probe.Second ?? probe.Asked) == player)
                {
                    probe.Due = _clock();
                    Schedule(probe.Due);
                }
            }
        }
    }

    /// <summary>Takes out every probe past its deadline. Costs one time check until one is due.</summary>
    public IReadOnlyList<Probe> TakeDue()
    {
        lock (_lock)
        {
            var now = _clock();

            if (now < _nextDue)
            {
                return Array.Empty<Probe>();
            }

            var due = _pending.Values.Where(p => p.Due <= now).ToList();

            foreach (var probe in due)
            {
                _pending.Remove(probe.Entity.Id);
            }

            _nextDue = _pending.Count == 0 ? DateTime.MaxValue : _pending.Values.Min(p => p.Due);
            return due;
        }
    }

    /// <summary>Puts a probe back, now waiting on a second player.</summary>
    public void AskNext(Probe probe, byte next)
    {
        lock (_lock)
        {
            probe.Second = next;
            probe.Due = _clock() + Wait;
            _pending[probe.Entity.Id] = probe;
            Schedule(probe.Due);
        }
    }

    private void Schedule(DateTime due)
    {
        if (due < _nextDue)
        {
            _nextDue = due;
        }
    }
}
