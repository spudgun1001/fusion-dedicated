namespace FusionDedicated.Server;

/// <summary>
/// When each player was last told again that they own an entity they asked for once more. A client
/// asks on every impact, so a repeat is answered at most once every half second.
/// </summary>
public sealed class OwnershipConfirmations
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly Func<DateTime> _clock;
    private readonly Dictionary<(byte Player, ushort Entity), DateTime> _sent = new();

    /// <summary>Forgetting runs on whichever thread removed the entity, while the loop answers requests.</summary>
    private readonly object _lock = new();

    public OwnershipConfirmations(Func<DateTime> clock) => _clock = clock;

    /// <summary>Whether a repeat from this player for this entity is answered now.</summary>
    public bool ShouldConfirm(byte player, ushort entity)
    {
        DateTime now = _clock();
        var key = (player, entity);

        lock (_lock)
        {
            if (_sent.TryGetValue(key, out var last) && now - last < Interval)
            {
                return false;
            }

            _sent[key] = now;
            return true;
        }
    }

    public void ForgetPlayer(byte player)
    {
        lock (_lock)
        {
            foreach (var key in _sent.Keys.Where(k => k.Player == player).ToList())
            {
                _sent.Remove(key);
            }
        }
    }

    public void ForgetEntity(ushort entity)
    {
        lock (_lock)
        {
            foreach (var key in _sent.Keys.Where(k => k.Entity == entity).ToList())
            {
                _sent.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _sent.Clear();
        }
    }
}
