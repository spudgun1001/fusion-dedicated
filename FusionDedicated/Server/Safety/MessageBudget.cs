namespace FusionDedicated.Server.Safety;

/// <summary>The messages a player may send only so many of a second.</summary>
public enum MessageKind
{
    Metadata,
    Avatar,
    Rpc,
}

/// <summary>
/// Per-player allowances for messages every other client is sent a copy of. What
/// goes over is counted and summed up once a minute rather than logged one by one.
/// </summary>
public sealed class MessageBudget
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SummaryAfter = TimeSpan.FromMinutes(1);

    private sealed class Counter
    {
        public DateTime WindowStart;
        public int Used;
        public DateTime FirstDrop;
        public int Dropped;
    }

    private readonly ServerConfig _config;
    private readonly Dictionary<(byte SmallId, MessageKind Kind), Counter> _counters = new();
    private readonly object _lock = new();

    public MessageBudget(ServerConfig config)
    {
        _config = config;
    }

    /// <summary>The allowance a second for one kind. Zero or less means no limit.</summary>
    public int LimitFor(MessageKind kind) => kind switch
    {
        MessageKind.Metadata => _config.MetadataPerSecond,
        MessageKind.Avatar => _config.AvatarSwapsPerSecond,
        _ => _config.RpcMessagesPerSecond,
    };

    public static string Word(MessageKind kind) => kind switch
    {
        MessageKind.Metadata => "metadata",
        MessageKind.Avatar => "avatar",
        _ => "RPC",
    };

    public bool Allow(byte smallId, MessageKind kind, DateTime now)
    {
        int limit = LimitFor(kind);

        if (limit <= 0)
        {
            return true;
        }

        lock (_lock)
        {
            if (!_counters.TryGetValue((smallId, kind), out var counter))
            {
                counter = new Counter { WindowStart = now };
                _counters[(smallId, kind)] = counter;
            }

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

            if (counter.Dropped == 0)
            {
                counter.FirstDrop = now;
            }

            counter.Dropped++;
            return false;
        }
    }

    /// <summary>
    /// Drop counts whose first drop was a minute ago or more, one per player and kind.
    /// Each count starts again from zero once it is returned.
    /// </summary>
    public IReadOnlyList<(byte SmallId, MessageKind Kind, int Dropped)> DueSummaries(DateTime now)
    {
        lock (_lock)
        {
            var due = new List<(byte SmallId, MessageKind Kind, int Dropped)>();

            foreach (var (key, counter) in _counters)
            {
                if (counter.Dropped > 0 && now - counter.FirstDrop >= SummaryAfter)
                {
                    due.Add((key.SmallId, key.Kind, counter.Dropped));
                    counter.Dropped = 0;
                }
            }

            return due;
        }
    }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            foreach (var key in _counters.Keys.Where(k => k.SmallId == smallId).ToList())
            {
                _counters.Remove(key);
            }
        }
    }
}
