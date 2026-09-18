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

    /// <summary>What became of one message the outbox tried to send.</summary>
    private enum Outcome
    {
        /// <summary>The builder had nothing, so nothing is owed.</summary>
        Nothing,
        Sent,

        /// <summary>The player's connection is backed up, so it waits here rather than behind that.</summary>
        Held,
    }

    private readonly Func<int> _perSecond;
    private readonly Func<DateTime> _clock;
    private readonly Func<ConnectedPlayer, byte[], bool, bool> _send;
    private readonly Action<string>? _warn;
    private readonly Dictionary<byte, Lane> _lanes = new();

    /// <summary>A kick forgets a player off the message loop, while the loop queues and pumps.</summary>
    private readonly object _lock = new();

    /// <param name="perSecond">Read on every call, so a changed setting applies at once. Zero or less means no pacing.</param>
    /// <param name="send">Returns false when the player cannot take it yet, which keeps it queued here.</param>
    /// <param name="warn">Told about a builder that threw, instead of the throw reaching the caller.</param>
    public CatchupOutbox(Func<int> perSecond, Func<DateTime> clock, Func<ConnectedPlayer, byte[], bool, bool> send,
        Action<string>? warn = null)
    {
        _perSecond = perSecond;
        _clock = clock;
        _send = send;
        _warn = warn;
    }

    /// <summary>A fixed message, for a sender that has nothing to re-check at send time.</summary>
    public void Enqueue(ConnectedPlayer player, byte[] message, bool reliable)
        => Enqueue(player, () => message, reliable);

    /// <summary>Sends now if nothing waits and the allowance covers it, otherwise queues the builder to run later.</summary>
    public void Enqueue(ConnectedPlayer player, Func<byte[]?> build, bool reliable)
    {
        int rate = _perSecond();

        lock (_lock)
        {
            if (rate <= 0)
            {
                // Anything already queued goes first, so order still holds once pacing is off.
                var unpaced = LaneFor(player.SmallId, rate);
                Drain(unpaced);

                if (unpaced.Waiting.Count == 0 && Send(player, build, reliable) != Outcome.Held)
                {
                    return;
                }

                unpaced.Waiting.Enqueue((player, build, reliable));
                return;
            }

            var lane = LaneFor(player.SmallId, rate);
            Refill(lane, rate);

            if (lane.Waiting.Count == 0 && lane.Tokens >= 1)
            {
                var outcome = Send(player, build, reliable);

                if (outcome == Outcome.Sent)
                {
                    lane.Tokens -= 1;
                }

                if (outcome != Outcome.Held)
                {
                    return;
                }
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
            // A snapshot, since a builder can forget or clear a lane mid pass and the
            // live dictionary would then have changed under the enumerator.
            foreach (byte smallId in _lanes.Keys.ToList())
            {
                if (!_lanes.TryGetValue(smallId, out var lane) || lane.Waiting.Count == 0)
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
                // behind it needs. Peeked rather than taken, so a message the player cannot
                // take yet stays here instead of being handed over and lost.
                while (lane.Waiting.Count > 0 && lane.Tokens >= 1)
                {
                    var next = lane.Waiting.Peek();
                    var outcome = Send(next.Player, next.Build, next.Reliable);

                    if (outcome == Outcome.Held)
                    {
                        break;
                    }

                    lane.Waiting.Dequeue();

                    if (outcome == Outcome.Sent)
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
        while (lane.Waiting.TryPeek(out var next))
        {
            if (Send(next.Player, next.Build, next.Reliable) == Outcome.Held)
            {
                return;
            }

            lane.Waiting.Dequeue();
        }
    }

    /// <summary>Builds and sends, both under the lock, so nothing else observes half a state change.</summary>
    private Outcome Send(ConnectedPlayer player, Func<byte[]?> build, bool reliable)
    {
        byte[]? message;

        try
        {
            message = build();
        }
        catch (Exception ex)
        {
            _warn?.Invoke($"Catch-up builder for player {player.SmallId} threw: {ex.Message}");
            return Outcome.Nothing;
        }

        if (message == null)
        {
            return Outcome.Nothing;
        }

        return _send(player, message, reliable) ? Outcome.Sent : Outcome.Held;
    }
}
