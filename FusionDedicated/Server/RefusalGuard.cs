namespace FusionDedicated.Server;

/// <summary>What to do with one refused request.</summary>
/// <param name="Suppressed">How many like it went unlogged before this one.</param>
public readonly record struct RefusalVerdict(bool Log, int Suppressed, bool Kick);

/// <summary>
/// Refused requests from each player: which get a log line, and whether the player is
/// flooding. A modded client sent 3,400 refused spawns a second, and a line for each
/// held up the whole server.
/// </summary>
public sealed class RefusalGuard
{
    private readonly int _kickPerSecond;
    private readonly TimeSpan _logWindow;
    private readonly Dictionary<(byte Player, string Kind), (DateTime Started, int Held)> _windows = new();
    private readonly Dictionary<byte, (DateTime Second, int Count, bool Kicked)> _rates = new();
    private readonly object _lock = new();

    /// <param name="kickPerSecond">Zero never kicks.</param>
    /// <param name="logWindow">How long after a logged refusal the same kind from the same player is only counted.</param>
    public RefusalGuard(int kickPerSecond, TimeSpan logWindow)
    {
        _kickPerSecond = kickPerSecond;
        _logWindow = logWindow;
    }

    public RefusalVerdict Note(byte player, string kind, DateTime now)
    {
        lock (_lock)
        {
            bool log;
            int suppressed = 0;

            if (_windows.TryGetValue((player, kind), out var window) && now - window.Started < _logWindow)
            {
                _windows[(player, kind)] = (window.Started, window.Held + 1);
                log = false;
            }
            else
            {
                suppressed = window.Held;
                _windows[(player, kind)] = (now, 0);
                log = true;
            }

            var rate = _rates.TryGetValue(player, out var previous) && now - previous.Second < TimeSpan.FromSeconds(1)
                ? (previous.Second, Count: previous.Count + 1, previous.Kicked)
                : (Second: now, Count: 1, previous.Kicked);

            bool kick = _kickPerSecond > 0 && rate.Count > _kickPerSecond && !rate.Kicked;

            _rates[player] = (rate.Second, rate.Count, rate.Kicked || kick);

            return new RefusalVerdict(log, suppressed, kick);
        }
    }

    /// <summary>Windows that have closed with refusals held back, for one summary line each.</summary>
    public IReadOnlyList<(byte Player, string Kind, int Count)> Flush(DateTime now)
    {
        lock (_lock)
        {
            var closed = _windows.Where(w => now - w.Value.Started >= _logWindow).ToList();

            foreach (var window in closed)
            {
                _windows.Remove(window.Key);
            }

            return closed
                .Where(w => w.Value.Held > 0)
                .Select(w => (w.Key.Player, w.Key.Kind, w.Value.Held))
                .ToList();
        }
    }

    public void Forget(byte player)
    {
        lock (_lock)
        {
            foreach (var key in _windows.Keys.Where(k => k.Player == player).ToList())
            {
                _windows.Remove(key);
            }

            _rates.Remove(player);
        }
    }
}
