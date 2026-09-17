namespace FusionDedicated.Server;

/// <summary>
/// Spots an item being pulled out of a slot and asked for again.
///
/// Two short memories, because for one request a copy and a loot drop look the same.
/// An item is remembered for a moment after it is drawn, since the copy request and
/// the game's own slot drop are sent by two different mods on the same grab and
/// arrive in whichever order those mods happen to run in. A match is remembered as
/// well, so the game naming something the player already carries is allowed once and
/// only a repeat is refused.
/// </summary>
public sealed class HolsterDuplicates
{
    /// <summary>Draws and barcodes kept for one player, after which the oldest goes.</summary>
    private const int MaxRemembered = 16;

    private readonly record struct Draw(string Barcode, byte Index, DateTime At);

    private sealed class Tracker
    {
        public readonly List<Draw> Drawn = new();
        public readonly Dictionary<string, (DateTime At, int Count)> Matched = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<byte, Tracker> _byPlayer = new();
    private readonly object _lock = new();

    /// <summary>Remembers what a player has just taken out of a slot.</summary>
    public void Drew(byte smallId, string barcode, byte index, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        lock (_lock)
        {
            var tracker = TrackerFor(smallId);

            Forget(tracker, barcode);

            if (tracker.Drawn.Count >= MaxRemembered)
            {
                tracker.Drawn.RemoveAt(0);
            }

            tracker.Drawn.Add(new Draw(barcode, index, now));
        }
    }

    /// <summary>Forgets a draw, because the item is back in a slot and the slots know it.</summary>
    public void Holstered(byte smallId, string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        lock (_lock)
        {
            if (_byPlayer.TryGetValue(smallId, out var tracker))
            {
                Forget(tracker, barcode);
            }
        }
    }

    /// <summary>The slot a player drew this barcode from inside <paramref name="window"/>, if any.</summary>
    public byte? DrawnFrom(byte smallId, string barcode, DateTime now, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        lock (_lock)
        {
            if (!_byPlayer.TryGetValue(smallId, out var tracker))
            {
                return null;
            }

            tracker.Drawn.RemoveAll(d => now - d.At > window);

            foreach (var draw in tracker.Drawn)
            {
                if (string.Equals(draw.Barcode, barcode, StringComparison.OrdinalIgnoreCase))
                {
                    return draw.Index;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Counts one match of a barcode. The first inside <paramref name="window"/> is as
    /// likely to be the game as a copy, so it is the second and later that are worth
    /// refusing.
    /// </summary>
    public int Note(byte smallId, string barcode, DateTime now, TimeSpan window)
    {
        lock (_lock)
        {
            var tracker = TrackerFor(smallId);

            foreach (string stale in tracker.Matched
                         .Where(m => now - m.Value.At > window)
                         .Select(m => m.Key)
                         .ToList())
            {
                tracker.Matched.Remove(stale);
            }

            int count = tracker.Matched.TryGetValue(barcode, out var seen) ? seen.Count + 1 : 1;

            if (count > 1 || tracker.Matched.Count < MaxRemembered)
            {
                tracker.Matched[barcode] = (now, count);
            }

            return count;
        }
    }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            _byPlayer.Remove(smallId);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _byPlayer.Clear();
        }
    }

    private Tracker TrackerFor(byte smallId)
    {
        if (!_byPlayer.TryGetValue(smallId, out var tracker))
        {
            tracker = new Tracker();
            _byPlayer[smallId] = tracker;
        }

        return tracker;
    }

    private static void Forget(Tracker tracker, string barcode)
        => tracker.Drawn.RemoveAll(d => string.Equals(d.Barcode, barcode, StringComparison.OrdinalIgnoreCase));
}
