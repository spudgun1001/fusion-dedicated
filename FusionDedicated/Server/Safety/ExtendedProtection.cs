namespace FusionDedicated.Server.Safety;

/// <summary>
/// Hard per-player ceiling on spawns per second. The burst guard tolerates a human
/// emptying a spawn menu; this catches automated loops, which fire at rates no
/// person reaches. Time is passed in so it tests without waiting.
/// </summary>
public sealed class SpawnRateLimiter
{
    private readonly Dictionary<byte, List<DateTime>> _hits = new();
    private readonly object _lock = new();

    private int _maxPerSecond;

    public SpawnRateLimiter(int maxPerSecond)
    {
        _maxPerSecond = maxPerSecond;
    }

    /// <summary>
    /// Changes the cap and keeps what everybody has already spent.
    ///
    /// This used to be a new object every time the settings were pushed, which
    /// is every join, every leave and every kick. Each one wiped the history, so
    /// the per-second cap reset for the whole server whenever anybody came or
    /// went, which is exactly when somebody spamming spawns benefits from it.
    /// </summary>
    public void SetLimit(int maxPerSecond)
    {
        lock (_lock)
        {
            _maxPerSecond = maxPerSecond;
        }
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

/// <param name="Report">
/// Whether this refusal is worth saying out loud. A nickname changer retries
/// several times a second for as long as it is switched on, and one line each
/// buried everything else in the console.
/// </param>
/// <param name="Silenced">How many refusals went unsaid since the last one that was.</param>
public readonly record struct NicknameVerdict(
    bool Allowed, string Reason, bool Report = false, int Silenced = 0);

/// <summary>
/// Refuses reserved nicknames and rapid renaming, which are how impersonation and
/// name-flicker griefing work.
/// </summary>
public sealed class NicknameGuard
{
    private readonly Dictionary<byte, List<DateTime>> _changes = new();
    private readonly Dictionary<byte, (DateTime Said, int Since)> _refusals = new();
    private readonly object _lock = new();

    /// <summary>How long a player's refusals stay quiet after one is reported.</summary>
    private static readonly TimeSpan SayAgainAfter = TimeSpan.FromSeconds(60);

    private int _maxPerMinute;
    private HashSet<string> _reserved;

    public NicknameGuard(int maxChangesPerMinute, IEnumerable<string> reserved)
    {
        _maxPerMinute = maxChangesPerMinute;
        _reserved = new HashSet<string>(
            reserved.Select(Normalise), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Changes the limits and keeps everybody's history, as above.</summary>
    public void SetLimits(int maxChangesPerMinute, IEnumerable<string> reserved)
    {
        lock (_lock)
        {
            _maxPerMinute = maxChangesPerMinute;
            _reserved = new HashSet<string>(
                reserved.Select(Normalise), StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Normalise(string name) => name.Trim();

    public NicknameVerdict Allow(byte smallId, string nickname, DateTime now)
    {
        // One lock for the whole thing. The reserved list and the limit are both
        // replaced when settings are pushed, from the panel's own thread, so
        // reading either outside it reads a field another thread is assigning.
        lock (_lock)
        {
            if (_reserved.Contains(Normalise(nickname)))
            {
                return Refuse(smallId, $"'{nickname.Trim()}' is a reserved name", now);
            }

            if (_maxPerMinute <= 0)
            {
                return new NicknameVerdict(true, "");
            }

            if (!_changes.TryGetValue(smallId, out var times))
            {
                times = new List<DateTime>(4);
                _changes[smallId] = times;
            }

            times.RemoveAll(t => (now - t).TotalSeconds >= 60.0);

            if (times.Count >= _maxPerMinute)
            {
                return Refuse(smallId, "changing nickname too often", now);
            }

            times.Add(now);
            return new NicknameVerdict(true, "");
        }
    }

    /// <summary>
    /// Turns a refusal down, so a player who keeps trying is heard once a minute
    /// rather than on every attempt.
    /// </summary>
    private NicknameVerdict Refuse(byte smallId, string reason, DateTime now)
    {
        _refusals.TryGetValue(smallId, out var last);

        if (last.Said != default && now - last.Said < SayAgainAfter)
        {
            _refusals[smallId] = (last.Said, last.Since + 1);
            return new NicknameVerdict(false, reason);
        }

        _refusals[smallId] = (now, 0);

        return new NicknameVerdict(false, reason, Report: true, Silenced: last.Since);
    }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            _changes.Remove(smallId);
            _refusals.Remove(smallId);
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
