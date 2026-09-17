namespace FusionDedicated.Server.Safety;

/// <summary>
/// Per-second allowances for hits from one player on another. What is dropped is counted
/// and summed up once a minute rather than logged one by one.
/// </summary>
public sealed class HitBudget
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SummaryAfter = TimeSpan.FromMinutes(1);

    private sealed class Counter
    {
        public DateTime WindowStart;
        public int Used;
        public DateTime FirstDrop;
        public int Dropped;
        public int WhileHolding;
    }

    private readonly ServerConfig _config;
    private readonly Dictionary<(byte Attacker, byte Target), Counter> _counters = new();
    private readonly object _lock = new();

    public HitBudget(ServerConfig config)
    {
        _config = config;
    }

    public bool Allow(byte attacker, byte target, DateTime now)
    {
        int limit = _config.HitsPerSecond;

        if (limit <= 0)
        {
            return true;
        }

        lock (_lock)
        {
            var counter = CounterFor(attacker, target, now);

            // A clock that went backwards starts a new second as well.
            if (now < counter.WindowStart || now - counter.WindowStart >= OneSecond)
            {
                counter.WindowStart = now;
                counter.Used = 0;
            }

            if (counter.Used < limit)
            {
                counter.Used++;
                return true;
            }

            CountDrop(counter, now);
            return false;
        }
    }

    /// <summary>Counts a hit dropped because the attacker was holding the target.</summary>
    public void DropWhileHolding(byte attacker, byte target, DateTime now)
    {
        lock (_lock)
        {
            var counter = CounterFor(attacker, target, now);
            CountDrop(counter, now);
            counter.WhileHolding++;
        }
    }

    /// <summary>
    /// Drop counts whose first drop was a minute ago or more, one per pair of players.
    /// Each count starts again from zero once it is returned.
    /// </summary>
    public IReadOnlyList<(byte Attacker, byte Target, int Dropped, int WhileHolding)> DueSummaries(DateTime now)
    {
        lock (_lock)
        {
            var due = new List<(byte Attacker, byte Target, int Dropped, int WhileHolding)>();

            foreach (var (key, counter) in _counters)
            {
                if (counter.Dropped > 0 && now - counter.FirstDrop >= SummaryAfter)
                {
                    due.Add((key.Attacker, key.Target, counter.Dropped, counter.WhileHolding));
                    counter.Dropped = 0;
                    counter.WhileHolding = 0;
                }
            }

            return due;
        }
    }

    /// <summary>Forgets a player on either side of a hit, since their small id will be reused.</summary>
    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            foreach (var key in _counters.Keys.Where(k => k.Attacker == smallId || k.Target == smallId).ToList())
            {
                _counters.Remove(key);
            }
        }
    }

    private Counter CounterFor(byte attacker, byte target, DateTime now)
    {
        if (!_counters.TryGetValue((attacker, target), out var counter))
        {
            counter = new Counter { WindowStart = now };
            _counters[(attacker, target)] = counter;
        }

        return counter;
    }

    private static void CountDrop(Counter counter, DateTime now)
    {
        if (counter.Dropped == 0)
        {
            counter.FirstDrop = now;
        }

        counter.Dropped++;
    }
}
