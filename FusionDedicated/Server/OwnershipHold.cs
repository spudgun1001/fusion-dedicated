namespace FusionDedicated.Server;

/// <summary>
/// When each entity last changed hands. Nine clients that all believed they owned one van
/// asked on every physics contact, and the 58 owner changes a second that came of it snapped
/// it between their copies, which is what a vehicle flinging about looks like.
/// </summary>
public sealed class OwnershipHold
{
    private readonly Func<DateTime> _clock;
    private readonly Func<int> _milliseconds;
    private readonly Dictionary<ushort, DateTime> _changed = new();

    /// <summary>Forgetting runs on whichever thread removed the entity, while the loop answers requests.</summary>
    private readonly object _lock = new();

    /// <param name="milliseconds">How long an entity stays put after a change. Zero or less holds nothing.</param>
    public OwnershipHold(Func<DateTime> clock, Func<int> milliseconds)
    {
        _clock = clock;
        _milliseconds = milliseconds;
    }

    /// <summary>Whether this entity changed hands too recently to change again.</summary>
    public bool Holding(ushort entity)
    {
        int window = _milliseconds();

        if (window <= 0)
        {
            return false;
        }

        DateTime now = _clock();

        lock (_lock)
        {
            // A clock that went backwards ends the hold rather than extending it for good.
            return _changed.TryGetValue(entity, out var last)
                && now >= last
                && now - last < TimeSpan.FromMilliseconds(window);
        }
    }

    public void Note(ushort entity)
    {
        DateTime now = _clock();

        lock (_lock)
        {
            _changed[entity] = now;
        }
    }

    public void ForgetEntity(ushort entity)
    {
        lock (_lock)
        {
            _changed.Remove(entity);
        }
    }
}
