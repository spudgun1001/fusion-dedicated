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
    public void Grab(byte player, byte hand, ushort entityId)
    {
        if (hand is not (LeftHand or RightHand))
        {
            return;
        }

        lock (_lock)
        {
            _held.RemoveAll(h => h.Player == player && h.Hand == hand);
            _held.Add(new HeldItem(player, hand, entityId));
        }
    }

    public void Release(byte player, byte hand)
    {
        lock (_lock)
        {
            _held.RemoveAll(h => h.Player == player && h.Hand == hand);
        }
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
