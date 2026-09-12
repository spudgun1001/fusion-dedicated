namespace FusionDedicated.Server;

/// <summary>
/// Keeps the pose-ownership log lines from flooding when the same problem
/// repeats on every network tick. Each key gets at most one line every ten
/// seconds.
/// </summary>
public sealed class PoseLogThrottle
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Dictionary<(ushort Entity, byte Sender), DateTime> _lastIgnored = new();
    private readonly Dictionary<ushort, DateTime> _lastKeptMoved = new();

    /// <summary>Whether enough time has passed since a key was last logged to log it again.</summary>
    public static bool ShouldLog(DateTime? last, DateTime now)
        => last is not { } previous || now - previous >= Window;

    /// <summary>Whether an ignored pose from this entity and sender may be logged now.</summary>
    public bool AllowIgnored(ushort entityId, byte sender, DateTime now)
    {
        var key = (entityId, sender);
        var last = _lastIgnored.TryGetValue(key, out var when) ? when : (DateTime?)null;

        if (!ShouldLog(last, now))
        {
            return false;
        }

        _lastIgnored[key] = now;
        return true;
    }

    /// <summary>Whether a kept prop having moved for this entity may be logged now.</summary>
    public bool AllowKeptMoved(ushort entityId, DateTime now)
    {
        var last = _lastKeptMoved.TryGetValue(entityId, out var when) ? when : (DateTime?)null;

        if (!ShouldLog(last, now))
        {
            return false;
        }

        _lastKeptMoved[entityId] = now;
        return true;
    }

    /// <summary>Drops every record for an entity that no longer exists, so a later id reusing it starts fresh.</summary>
    public void Forget(ushort entityId)
    {
        foreach (var key in _lastIgnored.Keys.Where(k => k.Entity == entityId).ToList())
        {
            _lastIgnored.Remove(key);
        }

        _lastKeptMoved.Remove(entityId);
    }
}
