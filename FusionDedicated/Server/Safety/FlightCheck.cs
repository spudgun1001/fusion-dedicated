namespace FusionDedicated.Server.Safety;

public enum FlightKind
{
    None,
    Climb,
    Descent,
}

/// <summary>A flight the server called, and how fast it was going in metres a second.</summary>
public readonly record struct FlightVerdict(FlightKind Kind, float Speed);

/// <summary>
/// Spots a player flying from their rig's height alone, since a fly mod's gun is spawned on their own
/// machine and never reaches the server.
///
/// Whatever throws a player about, gravity pulls them at about 9.8 m/s squared all the while: a jump, a
/// fling off an explosion and a fall off a building all speed up or slow down at that rate, which is about
/// 5 m/s inside each quarter of the window. Flying holds one speed instead, so the window is split in four
/// and a flight is one that moves at nearly the same speed in all of them. A fall that goes on long enough
/// to stop speeding up is holding a speed far above anybody's flight.
/// </summary>
public sealed class FlightCheck
{
    /// <summary>How much the speed may vary across the window's quarters. Gravity changes it by about 5.</summary>
    private const float SteadySpread = 3f;

    private const int Quarters = 4;

    private readonly ServerConfig _config;
    private readonly Dictionary<byte, List<(DateTime At, float Height)>> _heights = new();
    private readonly Dictionary<byte, List<DateTime>> _strikes = new();
    private readonly object _lock = new();

    public FlightCheck(ServerConfig config)
    {
        _config = config;
    }

    /// <summary>Takes one pose's height. Returns the flight it makes, once per window rather than per pose.</summary>
    public FlightVerdict Note(byte smallId, float height, DateTime now)
    {
        var none = new FlightVerdict(FlightKind.None, 0f);

        if (_config.FlightSpeed <= 0)
        {
            return none;
        }

        var window = TimeSpan.FromSeconds(Math.Max(0.5, _config.FlightWindowSeconds));

        lock (_lock)
        {
            if (!_heights.TryGetValue(smallId, out var heights))
            {
                heights = new List<(DateTime, float)>();
                _heights[smallId] = heights;
            }

            // A clock that went backwards starts the window again.
            if (heights.Count > 0 && now < heights[^1].At)
            {
                heights.Clear();
            }

            heights.Add((now, height));
            heights.RemoveAll(h => now - h.At > window * 2);

            int oldest = heights.FindLastIndex(h => now - h.At >= window);

            if (oldest < 0)
            {
                return none;
            }

            var verdict = Judge(heights, oldest, now, height);

            if (verdict.Kind != FlightKind.None)
            {
                // The next call needs a fresh window, so a long flight is a few reports rather than hundreds.
                heights.Clear();
            }

            return verdict;
        }
    }

    /// <summary>Records a flight against a player. Returns true when it brings them to the kick.</summary>
    public bool Strike(byte smallId, DateTime now)
    {
        int limit = _config.FlightStrikesBeforeKick;

        if (limit <= 0)
        {
            return false;
        }

        var window = TimeSpan.FromSeconds(Math.Max(1, _config.FlightStrikeWindowSeconds));

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
            _heights.Remove(smallId);
            _strikes.Remove(smallId);
        }
    }

    private FlightVerdict Judge(List<(DateTime At, float Height)> heights, int oldest, DateTime now, float height)
    {
        var start = heights[oldest];
        float span = (float)(now - start.At).TotalSeconds;

        if (span <= 0)
        {
            return new FlightVerdict(FlightKind.None, 0f);
        }

        float speed = (height - start.Height) / span;
        var none = new FlightVerdict(FlightKind.None, 0f);

        if (Math.Abs(speed) < _config.FlightSpeed)
        {
            return none;
        }

        // A fall long enough to stop speeding up holds a speed nobody flies at.
        if (speed < 0 && -speed > _config.FlightMaxFallSpeed)
        {
            return none;
        }

        // Gravity is still pulling them, so they are jumping, flung or falling rather than flying.
        int last = heights.Count - 1;
        float slowest = float.MaxValue;
        float fastest = float.MinValue;

        for (var quarter = 0; quarter < Quarters; quarter++)
        {
            var from = heights[(oldest * (Quarters - quarter) + last * quarter) / Quarters];
            var to = heights[(oldest * (Quarters - quarter - 1) + last * (quarter + 1)) / Quarters];

            if (to.At <= from.At)
            {
                return none;
            }

            float quarterSpeed = SpeedBetween(from, to);
            slowest = Math.Min(slowest, quarterSpeed);
            fastest = Math.Max(fastest, quarterSpeed);
        }

        if (fastest - slowest > SteadySpread)
        {
            return none;
        }

        return speed > 0
            ? new FlightVerdict(FlightKind.Climb, speed)
            : new FlightVerdict(FlightKind.Descent, -speed);
    }

    private static float SpeedBetween((DateTime At, float Height) from, (DateTime At, float Height) to)
    {
        float span = (float)(to.At - from.At).TotalSeconds;

        return span <= 0 ? 0f : (to.Height - from.Height) / span;
    }
}
