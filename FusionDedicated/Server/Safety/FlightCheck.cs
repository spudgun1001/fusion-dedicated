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
/// machine and never reaches the server. A jump is short, a ladder and a lift are slow, and a real fall
/// speeds up until it reaches its own steady speed, which is far faster than anybody flies.
/// </summary>
public sealed class FlightCheck
{
    /// <summary>How much faster a descent may get inside the window before it is a fall rather than a flight.</summary>
    private const float FallAcceleration = 4f;

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

        // Still going at the end of the window, so a jump that has peaked or a fall that has landed
        // is not taken for a flight on the way it started.
        var recent = heights[Math.Max(oldest, heights.Count - 1 - Math.Max(1, heights.Count / 8))];
        float tail = SpeedBetween(recent, (now, height));

        if (speed >= _config.FlightSpeed)
        {
            return tail >= _config.FlightSpeed
                ? new FlightVerdict(FlightKind.Climb, speed)
                : new FlightVerdict(FlightKind.None, 0f);
        }

        // A drop is only a flight while it holds one speed. A real fall keeps speeding up, and once it
        // stops it is already going faster than anybody flies.
        if (-speed < _config.FlightSpeed || -speed > _config.FlightMaxFallSpeed || -tail < _config.FlightSpeed)
        {
            return new FlightVerdict(FlightKind.None, 0f);
        }

        var middle = heights[(oldest + heights.Count - 1) / 2];
        float firstHalf = SpeedBetween(start, middle);
        float secondHalf = SpeedBetween(middle, (now, height));

        return firstHalf - secondHalf > FallAcceleration
            ? new FlightVerdict(FlightKind.None, 0f)
            : new FlightVerdict(FlightKind.Descent, -speed);
    }

    private static float SpeedBetween((DateTime At, float Height) from, (DateTime At, float Height) to)
    {
        float span = (float)(to.At - from.At).TotalSeconds;

        return span <= 0 ? 0f : (to.Height - from.Height) / span;
    }
}
