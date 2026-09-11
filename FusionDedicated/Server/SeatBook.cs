namespace FusionDedicated.Server;

/// <summary>A rider in a vehicle seat, and when they sat down.</summary>
public readonly record struct SeatRecord(byte Rider, ushort EntityId, byte Index, DateTime SeatedUtc);

/// <summary>
/// Who is sitting in which vehicle seat.
///
/// Fusion sends a seat once, when the rider sits, and a player who joins later is
/// never told. A rider is in one seat or none, and a seat holds one rider.
/// </summary>
public sealed class SeatBook
{
    private readonly List<SeatRecord> _seats = new();
    private readonly object _lock = new();

    /// <summary>
    /// Seats a rider, taking them out of any other seat and replacing whoever was
    /// recorded in this one.
    /// </summary>
    public void Ingress(byte rider, ushort entityId, byte index, DateTime now)
    {
        lock (_lock)
        {
            // The same seat again keeps the rider's place in the sit order.
            if (_seats.Exists(s => s.Rider == rider && s.EntityId == entityId && s.Index == index))
            {
                return;
            }

            _seats.RemoveAll(s => s.Rider == rider || (s.EntityId == entityId && s.Index == index));
            _seats.Add(new SeatRecord(rider, entityId, index, now));
        }
    }

    /// <summary>Takes a rider out of their seat.</summary>
    /// <returns>False when they were not in one.</returns>
    public bool Egress(byte rider)
    {
        lock (_lock)
        {
            return _seats.RemoveAll(s => s.Rider == rider) > 0;
        }
    }

    /// <summary>Forgets a rider who has left.</summary>
    /// <returns>How many seats were forgotten.</returns>
    public int ForgetRider(byte rider)
    {
        lock (_lock)
        {
            return _seats.RemoveAll(s => s.Rider == rider);
        }
    }

    /// <summary>Forgets every seat in a vehicle that no longer exists.</summary>
    /// <returns>How many seats were forgotten.</returns>
    public int ForgetEntity(ushort entityId)
    {
        lock (_lock)
        {
            return _seats.RemoveAll(s => s.EntityId == entityId);
        }
    }

    /// <summary>Everybody sitting in a vehicle, first to sit first.</summary>
    public IReadOnlyList<SeatRecord> RidersOf(ushort entityId)
    {
        lock (_lock)
        {
            return _seats.Where(s => s.EntityId == entityId).ToList();
        }
    }

    /// <summary>The seat a rider is in, or null.</summary>
    public SeatRecord? SeatOf(byte rider)
    {
        lock (_lock)
        {
            foreach (var seat in _seats)
            {
                if (seat.Rider == rider)
                {
                    return seat;
                }
            }

            return null;
        }
    }

    /// <summary>Whether anybody sits in a vehicle.</summary>
    public bool IsOccupied(ushort entityId)
    {
        lock (_lock)
        {
            return _seats.Exists(s => s.EntityId == entityId);
        }
    }

    /// <summary>Whether somebody is recorded in this seat of this vehicle.</summary>
    public bool IsRecorded(ushort entityId, byte index)
    {
        lock (_lock)
        {
            return _seats.Exists(s => s.EntityId == entityId && s.Index == index);
        }
    }

    /// <summary>Every seat taken, in sit order.</summary>
    public IReadOnlyList<SeatRecord> All()
    {
        lock (_lock)
        {
            return _seats.ToList();
        }
    }

    /// <summary>
    /// Whether a rider is too far from their seat to still be in it, which is how
    /// a missed egress shows up.
    /// </summary>
    public static bool IsStale(float riderX, float riderY, float riderZ,
        float seatX, float seatY, float seatZ, float max = 15f)
    {
        float dx = riderX - seatX;
        float dy = riderY - seatY;
        float dz = riderZ - seatZ;

        return dx * dx + dy * dy + dz * dz > max * max;
    }
}
