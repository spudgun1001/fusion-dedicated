namespace FusionDedicated.Server.Safety;

/// <summary>Avatars each player sent with impossible stats, and whether they have sent enough inside the window to be kicked.</summary>
public sealed class AvatarStrikes
{
    private readonly ServerConfig _config;
    private readonly Dictionary<byte, List<DateTime>> _strikes = new();
    private readonly object _lock = new();

    public AvatarStrikes(ServerConfig config)
    {
        _config = config;
    }

    /// <summary>Records a strike. Returns true when it brings them to the kick.</summary>
    public bool Strike(byte smallId, DateTime now)
    {
        int limit = _config.AvatarStrikesBeforeKick;

        if (limit <= 0)
        {
            return false;
        }

        var window = TimeSpan.FromSeconds(Math.Max(1, _config.AvatarStrikeWindowSeconds));

        lock (_lock)
        {
            if (!_strikes.TryGetValue(smallId, out var times))
            {
                times = new List<DateTime>(limit);
                _strikes[smallId] = times;
            }

            times.RemoveAll(t => now - t >= window || t > now);
            times.Add(now);

            return times.Count >= limit;
        }
    }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            _strikes.Remove(smallId);
        }
    }
}
