namespace FusionDedicated.Server;

/// <summary>An entity in one of a player's hands.</summary>
public readonly record struct HeldItem(byte Player, byte Hand, ushort EntityId);

/// <summary>
/// What each player is holding, followed from the grabs and releases passing
/// through. Written on the message loop and read from elsewhere, so it has its
/// own lock.
/// </summary>
public sealed class GrabBook
{
    private const byte LeftHand = 1;
    private const byte RightHand = 2;

    private readonly List<HeldItem> _held = new();
    private readonly object _lock = new();

    /// <summary>A new grab on a hand replaces whatever that hand held.</summary>
    /// <returns>What the hand let go of to make the grab, or null.</returns>
    public ushort? Grab(byte player, byte hand, ushort entityId)
    {
        if (hand is not (LeftHand or RightHand))
        {
            return null;
        }

        lock (_lock)
        {
            ushort? before = HeldIn(player, hand);

            _held.RemoveAll(h => h.Player == player && h.Hand == hand);
            _held.Add(new HeldItem(player, hand, entityId));

            return before == entityId ? null : before;
        }
    }

    /// <returns>What the hand held, or null when it was empty.</returns>
    public ushort? Release(byte player, byte hand)
    {
        lock (_lock)
        {
            ushort? before = HeldIn(player, hand);

            _held.RemoveAll(h => h.Player == player && h.Hand == hand);

            return before;
        }
    }

    private ushort? HeldIn(byte player, byte hand)
    {
        int index = _held.FindIndex(h => h.Player == player && h.Hand == hand);

        return index >= 0 ? _held[index].EntityId : null;
    }

    /// <returns>How many hands were emptied.</returns>
    public int ForgetPlayer(byte player)
    {
        lock (_lock)
        {
            return _held.RemoveAll(h => h.Player == player);
        }
    }

    /// <returns>How many hands were emptied.</returns>
    public int ForgetEntity(ushort entityId)
    {
        lock (_lock)
        {
            return _held.RemoveAll(h => h.EntityId == entityId);
        }
    }

    /// <summary>Empties every hand, for a level change, where clients send no releases.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _held.Clear();
        }
    }

    /// <summary>Who holds an entity, each player once, earliest grab first.</summary>
    public IReadOnlyList<byte> HoldersOf(ushort entityId)
    {
        lock (_lock)
        {
            return _held
                .Where(h => h.EntityId == entityId)
                .Select(h => h.Player)
                .Distinct()
                .ToList();
        }
    }

    public IReadOnlyList<HeldItem> All()
    {
        lock (_lock)
        {
            return _held.ToList();
        }
    }
}
