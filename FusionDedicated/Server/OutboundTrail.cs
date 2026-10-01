using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Server;

/// <summary>
/// The last few seconds of what was sent to each player. On 1 Oct players were dropped by their own game
/// a minute after somebody joined and nothing said why, so a player who drops is shown what reached them last.
/// </summary>
public sealed class OutboundTrail
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>A cap on each player's trail, so a flood cannot grow it without end.</summary>
    private const int Cap = 2048;

    private readonly record struct Sent(DateTime At, byte Tag, int Size, byte? From);

    private readonly Dictionary<uint, Queue<Sent>> _trails = new();
    private readonly object _lock = new();

    public void Note(uint connection, byte[] message, DateTime now)
    {
        if (message.Length == 0)
        {
            return;
        }

        var sent = new Sent(now, message[0], message.Length, FusionProtocol.SenderOf(message));

        lock (_lock)
        {
            if (!_trails.TryGetValue(connection, out var trail))
            {
                _trails[connection] = trail = new Queue<Sent>();
            }

            trail.Enqueue(sent);

            while (trail.Count > Cap || (trail.Count > 0 && now - trail.Peek().At > Window))
            {
                trail.Dequeue();
            }
        }
    }

    /// <summary>What reached a player in the last few seconds by type and sender, or null when nothing did.</summary>
    public string? Summary(uint connection, DateTime now, Func<byte, string> nameOf)
    {
        List<Sent> recent;

        lock (_lock)
        {
            if (!_trails.TryGetValue(connection, out var trail))
            {
                return null;
            }

            recent = trail.Where(s => now - s.At <= Window).ToList();
        }

        if (recent.Count == 0)
        {
            return null;
        }

        string Label(byte tag, byte? from)
        {
            string name = nameOf(tag);
            return $"tag {tag}{(name == "unknown" ? "" : " " + name)} from {(from is { } id ? $"player {id}" : "the server")}";
        }

        var groups = recent.GroupBy(s => (s.Tag, s.From))
            .OrderByDescending(g => g.Sum(s => s.Size))
            .Take(10)
            .Select(g => $"{Label(g.Key.Tag, g.Key.From)}: {g.Count()} ({g.Sum(s => s.Size) / 1024.0:0.0} KB)");

        var largest = recent.MaxBy(s => s.Size);

        return $"{recent.Count} messages, {recent.Sum(s => s.Size) / 1024.0:0.0} KB. {string.Join("; ", groups)}. " +
               $"The largest was {largest.Size / 1024.0:0.0} KB, largest {Label(largest.Tag, largest.From)}";
    }

    public void Forget(uint connection)
    {
        lock (_lock)
        {
            _trails.Remove(connection);
        }
    }
}
