namespace FusionDedicated.Server;

/// <summary>
/// Spots an item being pulled out of a slot and asked for again.
///
/// Two short memories, because for one request a copy and a loot drop look the same.
/// An item is remembered for a moment after it is drawn, since the copy request and
/// the game's own slot drop are sent by two different mods on the same grab and
/// arrive in either order. A barcode that has matched once is then suspected for a
/// while whatever the slots say, because the cheat puts its copy back itself and the
/// server is never told.
/// </summary>
public sealed class HolsterDuplicates
{
    /// <summary>
    /// Draws and suspicions kept per player, after which the least recently seen one
    /// goes. Dropping the new barcode instead would let a client pick sixteen it does
    /// not care about and hide the seventeenth behind them.
    /// </summary>
    public const int MaxRemembered = 16;

    private readonly record struct Draw(string Barcode, byte Index, DateTime At);

    private sealed class Tracker
    {
        public readonly List<Draw> Drawn = new();

        public readonly Dictionary<string, (DateTime At, int Count, byte Slot)> Suspect =
            new(StringComparer.OrdinalIgnoreCase);
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

            Drop(tracker, barcode);

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
                Drop(tracker, barcode);
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
    /// Whether this barcode is still suspected from an earlier match. The slots are no
    /// longer asked by then, since a copy goes back into one without the server hearing.
    /// </summary>
    public bool Armed(byte smallId, string barcode, DateTime now, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        lock (_lock)
        {
            return _byPlayer.TryGetValue(smallId, out var tracker)
                   && tracker.Suspect.TryGetValue(barcode, out var seen)
                   && now - seen.At <= window;
        }
    }

    /// <summary>
    /// Counts one spawn of a suspect barcode and arms it for another window. The first
    /// inside the window is as likely to be the game as a copy, so it is the second and
    /// later that are worth refusing.
    /// </summary>
    /// <param name="slot">The slot it is in now, or null to keep the one it was armed from.</param>
    public (int Attempt, byte Slot) Note(byte smallId, string barcode, byte? slot, DateTime now, TimeSpan window)
    {
        lock (_lock)
        {
            var tracker = TrackerFor(smallId);

            foreach (string stale in tracker.Suspect
                         .Where(s => now - s.Value.At > window)
                         .Select(s => s.Key)
                         .ToList())
            {
                tracker.Suspect.Remove(stale);
            }

            bool known = tracker.Suspect.TryGetValue(barcode, out var seen);
            int count = known ? seen.Count + 1 : 1;
            byte index = slot ?? (known ? seen.Slot : (byte)0);

            if (!known && tracker.Suspect.Count >= MaxRemembered)
            {
                tracker.Suspect.Remove(tracker.Suspect.OrderBy(s => s.Value.At).First().Key);
            }

            tracker.Suspect[barcode] = (now, count, index);

            return (count, index);
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

    private static void Drop(Tracker tracker, string barcode)
        => tracker.Drawn.RemoveAll(d => string.Equals(d.Barcode, barcode, StringComparison.OrdinalIgnoreCase));
}
