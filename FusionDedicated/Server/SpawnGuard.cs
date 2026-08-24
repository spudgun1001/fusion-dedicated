namespace FusionDedicated.Server;

/// <summary>
/// Watches for spawn floods.
///
/// A relay server is the wrong place to notice trouble by feeling slow: it never
/// simulates anything, so a thousand props cost it a dictionary entry each. Every
/// client, though, has to instantiate and simulate all of them — which is why a
/// flood shows up as the entire lobby dropping at once while the server sits at
/// idle. So the limits here are about what clients can survive, not what the
/// server can.
///
/// The response is graduated: early strikes only delete the offending props, and
/// a kick comes after repeated attempts.
/// </summary>
public sealed class SpawnGuard
{
    public sealed record Verdict(bool Allowed, bool Kick, bool Purge, string Reason);

    private static readonly Verdict Ok = new(true, false, false, "");

    private sealed class Tracker
    {
        public readonly Queue<DateTime> Recent = new();
        public int Strikes;
        public DateTime LastStrike = DateTime.MinValue;
    }

    private readonly ServerConfig _config;
    private readonly Dictionary<byte, Tracker> _byPlayer = new();
    private readonly object _lock = new();

    public SpawnGuard(ServerConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Set when an exempt player crossed a limit, so the caller can log it. Purely
    /// informational — nothing is enforced against an exempt player.
    /// </summary>
    public string? ExemptOverrun { get; private set; }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            _byPlayer.Remove(smallId);
        }
    }

    /// <summary>
    /// Decides what to do about one spawn request. <paramref name="ownedEntities"/> is
    /// how many the player already owns.
    /// </summary>
    public Verdict Check(ConnectedPlayer player, int ownedEntities)
    {
        if (!_config.AntiSpamEnabled)
        {
            return Ok;
        }

        if (player.Permission.IsAtLeast(_config.AntiSpamExemptLevel))
        {
            // Still worth saying out loud. Otherwise an exempt player testing the
            // guard sees nothing happen and reasonably concludes it does not work.
            ExemptOverrun = ownedEntities >= _config.MaxEntitiesPerPlayer
                ? $"{player.DisplayName} owns {ownedEntities} entities but is exempt " +
                  $"({player.Permission.ToFusionString()})"
                : null;

            return Ok;
        }

        ExemptOverrun = null;

        lock (_lock)
        {
            if (!_byPlayer.TryGetValue(player.SmallId, out var tracker))
            {
                tracker = new Tracker();
                _byPlayer[player.SmallId] = tracker;
            }

            var now = DateTime.UtcNow;
            var window = TimeSpan.FromSeconds(Math.Max(1, _config.SpawnWindowSeconds));

            // Strikes decay, so someone who behaves for a while starts clean again.
            if (tracker.Strikes > 0 && now - tracker.LastStrike > TimeSpan.FromMinutes(2))
            {
                tracker.Strikes = 0;
            }

            while (tracker.Recent.Count > 0 && now - tracker.Recent.Peek() > window)
            {
                tracker.Recent.Dequeue();
            }

            if (ownedEntities >= _config.MaxEntitiesPerPlayer)
            {
                return Strike(tracker, now,
                    $"owns {ownedEntities} entities, over the {_config.MaxEntitiesPerPlayer} limit");
            }

            if (tracker.Recent.Count >= _config.SpawnBurstLimit)
            {
                return Strike(tracker, now,
                    $"spawned {tracker.Recent.Count} items in {_config.SpawnWindowSeconds}s");
            }

            tracker.Recent.Enqueue(now);
            return Ok;
        }
    }

    private Verdict Strike(Tracker tracker, DateTime now, string what)
    {
        // At most one strike per window. While a burst is still inside the window
        // every following spawn also trips, so counting each one would burn through
        // the whole allowance in the same second and there would be no warning
        // phase at all — which is exactly what happened the first time this fired.
        var window = TimeSpan.FromSeconds(Math.Max(1, _config.SpawnWindowSeconds));

        if (now - tracker.LastStrike >= window)
        {
            tracker.Strikes++;
            tracker.LastStrike = now;
        }

        bool kick = tracker.Strikes >= Math.Max(1, _config.SpamStrikesBeforeKick);

        return new Verdict(
            Allowed: false,
            Kick: kick,
            Purge: true,
            Reason: kick
                ? $"{what} — strike {tracker.Strikes}, removing them"
                : $"{what} — strike {tracker.Strikes}, dropping the spawn");
    }
}
