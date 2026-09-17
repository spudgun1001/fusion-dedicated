namespace FusionDedicated.Server;

/// <summary>
/// When each far player was last sent each moving prop's pose, so they get only a few a second.
/// Written on the message loop, forgotten from wherever an entity or player goes.
/// </summary>
public sealed class PoseThinning
{
    private readonly Dictionary<(ushort Entity, byte Recipient), DateTime> _lastSent = new();
    private readonly object _lock = new();

    /// <summary>Whether a far player is due another pose of this prop, noting the send when they are.</summary>
    public bool ShouldSend(ushort entity, byte recipient, DateTime now, int perSecond)
    {
        var interval = TimeSpan.FromSeconds(1.0 / perSecond);

        lock (_lock)
        {
            // A clock that went backwards sends again, rather than holding poses back until it catches up.
            if (_lastSent.TryGetValue((entity, recipient), out var last) && now >= last && now - last < interval)
            {
                return false;
            }

            _lastSent[(entity, recipient)] = now;
            return true;
        }
    }

    public void ForgetEntity(ushort entity)
    {
        lock (_lock)
        {
            foreach (var key in _lastSent.Keys.Where(k => k.Entity == entity).ToList())
            {
                _lastSent.Remove(key);
            }
        }
    }

    public void ForgetPlayer(byte recipient)
    {
        lock (_lock)
        {
            foreach (var key in _lastSent.Keys.Where(k => k.Recipient == recipient).ToList())
            {
                _lastSent.Remove(key);
            }
        }
    }
}
