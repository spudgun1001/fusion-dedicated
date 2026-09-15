namespace FusionDedicated.Server;

/// <summary>
/// Spreads what a joining player is sent over time. Each player's messages leave in the
/// order they were queued, as fast as their allowance refills.
///
/// What is queued is a builder, not fixed bytes, so a message reflects the world as it is
/// when it actually leaves rather than as it was when it was queued.
/// </summary>
public sealed class CatchupOutbox
{
    private sealed class Lane
    {
        public readonly Queue<(ConnectedPlayer Player, Func<byte[]?> Build, bool Reliable)> Waiting = new();
        public double Tokens;
        public DateTime Refilled;
        public bool Reported;
    }

    private readonly Func<int> _perSecond;
    private readonly Func<DateTime> _clock;
    private readonly Action<ConnectedPlayer, byte[], bool> _send;
    private readonly Dictionary<byte, Lane> _lanes = new();

    /// <summary>A kick forgets a player off the message loop, while the loop queues and pumps.</summary>
    private readonly object _lock = new();

    /// <param name="perSecond">Read on every call, so a changed setting applies at once. Zero or less means no pacing.</param>
    public CatchupOutbox(Func<int> perSecond, Func<DateTime> clock, Action<ConnectedPlayer, byte[], bool> send)
    {
        _perSecond = perSecond;
        _clock = clock;
        _send = send;
    }

    /// <summary>A fixed message, for a sender that has nothing to re-check at send time.</summary>
    public void Enqueue(ConnectedPlayer player, byte[] message, bool reliable)
        => Enqueue(player, () => message, reliable);

    /// <summary>
    /// Sends at once while nothing waits for this player and their allowance lasts, otherwise
    /// queues the builder itself. Either way, the builder runs at the moment of sending, not now.
    /// </summary>
    public void Enqueue(ConnectedPlayer player, Func<byte[]?> build, bool reliable)
    {
        int rate = _perSecond();

        lock (_lock)
        {
            if (rate <= 0)
            {
                // Anything queued before pacing was turned off still goes first.
                if (_lanes.TryGetValue(player.SmallId, out var paced))
                {
                    Drain(paced);
                }

                Send(player, build, reliable);
                return;
            }

            var lane = LaneFor(player.SmallId, rate);
            Refill(lane, rate);

            if (lane.Waiting.Count == 0 && lane.Tokens >= 1)
            {
                // A build that turns out to have nothing to send costs no token: nothing left,
                // so there is nothing behind it in this lane to hold up either.
                if (Send(player, build, reliable))
                {
                    lane.Tokens -= 1;
                }

                return;
            }

            lane.Waiting.Enqueue((player, build, reliable));
        }
    }

    /// <summary>Sends whatever each player's allowance has refilled enough for.</summary>
    public void Pump()
    {
        int rate = _perSecond();

        lock (_lock)
        {
            foreach (var lane in _lanes.Values)
            {
                if (lane.Waiting.Count == 0)
                {
                    continue;
                }

                if (rate <= 0)
                {
                    Drain(lane);
                    continue;
                }

                Refill(lane, rate);

                // A null build is skipped for free, so it never costs the token a real message
                // behind it needs.
                while (lane.Waiting.Count > 0 && lane.Tokens >= 1)
                {
                    var next = lane.Waiting.Dequeue();

                    if (Send(next.Player, next.Build, next.Reliable))
                    {
                        lane.Tokens -= 1;
                    }
                }
            }
        }
    }

    public int Waiting(byte smallId)
    {
        lock (_lock)
        {
            return _lanes.TryGetValue(smallId, out var lane) ? lane.Waiting.Count : 0;
        }
    }

    /// <summary>How many messages wait for this player, the first time any do since they were forgotten or cleared.</summary>
    /// <returns>Zero when nothing waits or this backlog was already reported.</returns>
    public int BacklogToReport(byte smallId)
    {
        lock (_lock)
        {
            if (!_lanes.TryGetValue(smallId, out var lane) || lane.Reported || lane.Waiting.Count == 0)
            {
                return 0;
            }

            lane.Reported = true;
            return lane.Waiting.Count;
        }
    }

    public void Forget(byte smallId)
    {
        lock (_lock)
        {
            _lanes.Remove(smallId);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lanes.Clear();
        }
    }

    private Lane LaneFor(byte smallId, int rate)
    {
        if (!_lanes.TryGetValue(smallId, out var lane))
        {
            // Full, so a catch-up within the allowance goes out at once.
            lane = new Lane { Tokens = rate, Refilled = _clock() };
            _lanes[smallId] = lane;
        }

        return lane;
    }

    private void Refill(Lane lane, int rate)
    {
        var now = _clock();
        double seconds = Math.Max(0, (now - lane.Refilled).TotalSeconds);

        lane.Tokens = Math.Min(rate, lane.Tokens + (seconds * rate));
        lane.Refilled = now;
    }

    private void Drain(Lane lane)
    {
        while (lane.Waiting.TryDequeue(out var next))
        {
            Send(next.Player, next.Build, next.Reliable);
        }
    }

    /// <summary>Builds and sends, both under the lock, so nothing else observes half a state change.</summary>
    /// <returns>Whether the build produced anything to send.</returns>
    private bool Send(ConnectedPlayer player, Func<byte[]?> build, bool reliable)
    {
        byte[]? message = build();

        if (message == null)
        {
            return false;
        }

        _send(player, message, reliable);
        return true;
    }
}
