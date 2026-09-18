namespace FusionDedicated.Server;

/// <summary>
/// Reliable messages Steam refused, held per connection until its send buffer drains. Order holds
/// because once a connection has anything waiting, everything reliable for it queues behind that.
///
/// Every send in this server runs on the message loop under the world lock, so this keeps no lock
/// of its own.
/// </summary>
public sealed class SendRetry
{
    private sealed class Lane
    {
        public readonly Queue<byte[]> Waiting = new();
        public bool Reported;
    }

    private readonly Dictionary<uint, Lane> _lanes = new();

    /// <summary>Puts a message at the back of a connection's queue, dropping the oldest when it is full.</summary>
    /// <returns>How many were dropped to make room, and whether this is the first overflow since it drained.</returns>
    public (int Dropped, bool FirstOverflow) Queue(uint connection, byte[] message, int limit)
    {
        if (!_lanes.TryGetValue(connection, out var lane))
        {
            _lanes[connection] = lane = new Lane();
        }

        lane.Waiting.Enqueue(message);

        var dropped = 0;

        while (lane.Waiting.Count > limit)
        {
            lane.Waiting.Dequeue();
            dropped++;
        }

        if (dropped == 0)
        {
            return (0, false);
        }

        bool first = !lane.Reported;
        lane.Reported = true;

        return (dropped, first);
    }

    public int Waiting(uint connection)
        => _lanes.TryGetValue(connection, out var lane) ? lane.Waiting.Count : 0;

    public void Forget(uint connection) => _lanes.Remove(connection);

    /// <summary>Sends what each connection is holding, stopping on that connection at the first refusal.</summary>
    public void Drain(Func<uint, byte[], bool> send)
    {
        // A snapshot, since an emptied lane is forgotten as we go.
        foreach (uint connection in _lanes.Keys.ToList())
        {
            var lane = _lanes[connection];

            while (lane.Waiting.TryPeek(out byte[]? message) && send(connection, message))
            {
                lane.Waiting.Dequeue();
            }

            if (lane.Waiting.Count == 0)
            {
                _lanes.Remove(connection);
            }
        }
    }
}
