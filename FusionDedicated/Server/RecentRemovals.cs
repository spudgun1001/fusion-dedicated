namespace FusionDedicated.Server;

/// <summary>
/// The ids the server removed in the last five seconds, or the window given. A client resends a
/// despawn, and every copy for an id already gone went to everybody again.
/// </summary>
public sealed class RecentRemovals
{
    private readonly TimeSpan _window;

    private readonly Func<DateTime> _clock;
    private readonly Dictionary<ushort, DateTime> _removed = new();
    private readonly Queue<(ushort Id, DateTime At)> _order = new();

    /// <summary>A removal is noted on whichever thread removed the entity, while the loop reads them.</summary>
    private readonly object _lock = new();

    public RecentRemovals(Func<DateTime> clock, TimeSpan? window = null)
    {
        _clock = clock;
        _window = window ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>How many removals are held, expired ones included until the next prune.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _removed.Count;
            }
        }
    }

    public void Note(ushort id)
    {
        DateTime now = _clock();

        lock (_lock)
        {
            Prune(now);

            _removed[id] = now;
            _order.Enqueue((id, now));
        }
    }

    /// <summary>Whether this id was removed within the window.</summary>
    public bool Contains(ushort id)
    {
        DateTime now = _clock();

        lock (_lock)
        {
            Prune(now);

            return _removed.ContainsKey(id);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _removed.Clear();
            _order.Clear();
        }
    }

    private void Prune(DateTime now)
    {
        while (_order.TryPeek(out var oldest) && now - oldest.At >= _window)
        {
            _order.Dequeue();

            // An id removed again since has a later time, which keeps it.
            if (_removed.TryGetValue(oldest.Id, out var at) && at == oldest.At)
            {
                _removed.Remove(oldest.Id);
            }
        }
    }
}
