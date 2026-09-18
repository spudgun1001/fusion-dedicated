namespace FusionDedicated.Server;

/// <summary>
/// Who was refused which seat and when. Fusion re-registers a seat while the rider stands in
/// its trigger, so answering a refusal with an egress put one player in and out of a police
/// SUV about three times a second, 342 times in four minutes.
/// </summary>
public sealed class SeatRefusals
{
    /// <summary>The most seats one rider is remembered for, so the book cannot grow with their asking.</summary>
    public const int SeatsPerRider = 8;

    private readonly record struct Refusal(DateTime Refused, int Suppressed);

    private readonly Func<DateTime> _clock;
    private readonly Func<double> _seconds;
    private readonly Dictionary<(byte Rider, ushort Entity, byte Index), Refusal> _refused = new();

    /// <summary>Forgetting runs on whichever thread removed the entity, while the loop handles seats.</summary>
    private readonly object _lock = new();

    /// <param name="seconds">How long a refused rider is left alone. Zero or less cools nothing.</param>
    public SeatRefusals(Func<DateTime> clock, Func<double> seconds)
    {
        _clock = clock;
        _seconds = seconds;
    }

    /// <summary>Whether this rider was refused this seat too recently to be put to a plugin again.</summary>
    public bool Cooling(byte rider, ushort entity, byte index)
    {
        double seconds = _seconds();

        if (seconds <= 0)
        {
            return false;
        }

        DateTime now = _clock();
        var key = (rider, entity, index);

        lock (_lock)
        {
            // A clock that went backwards ends the cooldown rather than holding the seat shut for good.
            if (!_refused.TryGetValue(key, out var last)
                || now < last.Refused
                || now - last.Refused >= TimeSpan.FromSeconds(seconds))
            {
                return false;
            }

            _refused[key] = last with { Suppressed = last.Suppressed + 1 };
            return true;
        }
    }

    /// <summary>Remembers a refusal.</summary>
    /// <returns>How many attempts were dropped since the last one, for the log line.</returns>
    public int Note(byte rider, ushort entity, byte index)
    {
        DateTime now = _clock();
        var key = (rider, entity, index);

        lock (_lock)
        {
            int suppressed = _refused.TryGetValue(key, out var last) ? last.Suppressed : 0;
            _refused[key] = new Refusal(now, 0);

            var mine = _refused.Where(r => r.Key.Rider == rider).ToList();

            foreach (var stale in mine.OrderBy(r => r.Value.Refused).Take(mine.Count - SeatsPerRider))
            {
                _refused.Remove(stale.Key);
            }

            return suppressed;
        }
    }

    public void ForgetRider(byte rider)
    {
        lock (_lock)
        {
            foreach (var key in _refused.Keys.Where(k => k.Rider == rider).ToList())
            {
                _refused.Remove(key);
            }
        }
    }

    public void ForgetEntity(ushort entity)
    {
        lock (_lock)
        {
            foreach (var key in _refused.Keys.Where(k => k.Entity == entity).ToList())
            {
                _refused.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _refused.Clear();
        }
    }
}
