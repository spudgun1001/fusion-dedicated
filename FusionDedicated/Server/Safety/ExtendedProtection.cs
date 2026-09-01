namespace FusionDedicated.Server.Safety;

/// <summary>
/// Hard per-player ceiling on spawns per second. The burst guard tolerates a human
/// emptying a spawn menu; this catches automated loops, which fire at rates no
/// person reaches. Time is passed in so it tests without waiting.
/// </summary>
public sealed class SpawnRateLimiter
{
    private readonly int _maxPerSecond;
    private readonly Dictionary<byte, List<DateTime>> _hits = new();
    private readonly object _lock = new();

    public SpawnRateLimiter(int maxPerSecond)
    {
        _maxPerSecond = maxPerSecond;
    }

    public bool Allow(byte smallId, DateTime now)
    {
        if (_maxPerSecond <= 0)
        {
            return true;
        }

        lock (_lock)
        {
            if (!_hits.TryGetValue(smallId, out var times))
            {
                times = new List<DateTime>(8);
                _hits[smallId] = times;
            }

            times.RemoveAll(t => (now - t).TotalSeconds >= 1.0);

            if (times.Count >= _maxPerSecond)
            {
                return false;
            }

            times.Add(now);
            return true;
        }
    }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            _hits.Remove(smallId);
        }
    }
}

public readonly record struct NicknameVerdict(bool Allowed, string Reason);

/// <summary>
/// Refuses reserved nicknames and rapid renaming, which are how impersonation and
/// name-flicker griefing work.
/// </summary>
public sealed class NicknameGuard
{
    private readonly int _maxPerMinute;
    private readonly HashSet<string> _reserved;
    private readonly Dictionary<byte, List<DateTime>> _changes = new();
    private readonly object _lock = new();

    public NicknameGuard(int maxChangesPerMinute, IEnumerable<string> reserved)
    {
        _maxPerMinute = maxChangesPerMinute;
        _reserved = new HashSet<string>(
            reserved.Select(Normalise), StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalise(string name) => name.Trim();

    public NicknameVerdict Allow(byte smallId, string nickname, DateTime now)
    {
        if (_reserved.Contains(Normalise(nickname)))
        {
            return new NicknameVerdict(false, $"'{nickname.Trim()}' is a reserved name");
        }

        if (_maxPerMinute <= 0)
        {
            return new NicknameVerdict(true, "");
        }

        lock (_lock)
        {
            if (!_changes.TryGetValue(smallId, out var times))
            {
                times = new List<DateTime>(4);
                _changes[smallId] = times;
            }

            times.RemoveAll(t => (now - t).TotalSeconds >= 60.0);

            if (times.Count >= _maxPerMinute)
            {
                return new NicknameVerdict(false, "changing nickname too often");
            }

            times.Add(now);
            return new NicknameVerdict(true, "");
        }
    }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            _changes.Remove(smallId);
        }
    }
}

/// <summary>
/// Who may remove an entity. Without this any client can despawn anyone's props,
/// which is the cheapest grief there is.
/// </summary>
public static class DespawnAuthority
{
    public static bool MayDespawn(byte? entityOwner, byte requester, PermissionLevel requesterRank)
        => entityOwner is null
        || entityOwner == requester
        || requesterRank.IsAtLeast(PermissionLevel.Operator);
}
