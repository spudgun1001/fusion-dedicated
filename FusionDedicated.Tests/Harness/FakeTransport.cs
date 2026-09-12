using System.Diagnostics;
using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Harness;

/// <summary>Stands in for Steam: queues what fake players send and records what the server sends back.</summary>
public sealed class FakeTransport : ISocketTransport
{
    private readonly Queue<(HSteamNetConnection Connection, byte[] Message)> _inbox = new();
    private readonly Dictionary<uint, List<(byte[] Message, bool Reliable)>> _sent = new();
    private readonly object _lock = new();

    private Action<HSteamNetConnection, string> _closed = (_, _) => { };
    private uint _nextConnection = 1000;

    public ulong LocalSteamId => 90071992547409920;

    public List<(uint Connection, string? Reason)> Closed { get; } = new();

    /// <summary>How long the server took over each message it was handed.</summary>
    public List<TimeSpan> HandleTimes { get; } = new();

    public HSteamNetConnection Connect() => new(_nextConnection++);

    public void Deliver(HSteamNetConnection connection, byte[] message)
    {
        lock (_lock)
        {
            _inbox.Enqueue((connection, message));
        }
    }

    /// <summary>The peer going away, as Steam's status callback reports it.</summary>
    public void Disconnect(HSteamNetConnection connection, string reason) => _closed(connection, reason);

    public IReadOnlyList<(byte[] Message, bool Reliable)> SentTo(HSteamNetConnection connection)
    {
        lock (_lock)
        {
            return _sent.TryGetValue(connection.m_HSteamNetConnection, out var list) ? list.ToList() : new();
        }
    }

    public void Start(Action<HSteamNetConnection> connecting, Action<HSteamNetConnection, string> closed)
        => _closed = closed;

    public void Accept(HSteamNetConnection connection)
    {
    }

    public void Close(HSteamNetConnection connection, string? reason)
    {
        lock (_lock)
        {
            Closed.Add((connection.m_HSteamNetConnection, reason));

            // Steam delivers nothing once a connection is closed, so drop whatever
            // of theirs is still queued rather than handing it to Receive later.
            var kept = _inbox.Where(entry => entry.Connection.m_HSteamNetConnection != connection.m_HSteamNetConnection).ToList();
            _inbox.Clear();

            foreach (var entry in kept)
            {
                _inbox.Enqueue(entry);
            }
        }
    }

    public ulong RemoteSteamId(HSteamNetConnection connection) => 0;

    public bool Send(HSteamNetConnection connection, byte[] message, bool reliable)
    {
        lock (_lock)
        {
            if (!_sent.TryGetValue(connection.m_HSteamNetConnection, out var list))
            {
                _sent[connection.m_HSteamNetConnection] = list = new();
            }

            list.Add((message, reliable));
        }

        return true;
    }

    public int Receive(int max, Action<HSteamNetConnection, byte[]> handle)
    {
        int count = 0;

        while (count < max)
        {
            (HSteamNetConnection Connection, byte[] Message) next;

            lock (_lock)
            {
                if (_inbox.Count == 0)
                {
                    break;
                }

                next = _inbox.Dequeue();
            }

            var watch = Stopwatch.StartNew();
            handle(next.Connection, next.Message);
            HandleTimes.Add(watch.Elapsed);

            count++;
        }

        return count;
    }

    public void Dispose()
    {
    }
}
