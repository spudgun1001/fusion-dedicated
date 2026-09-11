namespace FusionDedicated.Server;

/// <summary>
/// Which weapon is in which body slot.
///
/// The rule that matters is that a weapon is in one slot or none. Taking
/// somebody's gun off their hip and putting it on your own arrives as an insert
/// naming the new slot and, often, nothing at all about the old one. Recording
/// both left the same gun on two people for anybody who joined afterwards.
/// </summary>
public sealed class HolsterSlots
{
    private readonly Dictionary<(ushort Slot, byte Index), ushort> _slots = new();

    /// <summary>Ten slots a player, so this is a great many players' worth.</summary>
    public const int Capacity = 2048;

    public int Count => _slots.Count;

    /// <summary>Everything held, for telling somebody who has just arrived.</summary>
    public IReadOnlyList<(ushort Slot, byte Index, ushort Weapon)> All()
        => _slots.Select(s => (s.Key.Slot, s.Key.Index, s.Value)).ToList();

    /// <summary>Where a weapon is, or null when it is not in a slot.</summary>
    public (ushort Slot, byte Index)? Find(ushort weapon)
    {
        foreach (var slot in _slots)
        {
            if (slot.Value == weapon)
            {
                return slot.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// Puts a weapon in a slot, taking it out of wherever it was.
    /// </summary>
    /// <returns>False when there was no room to record it.</returns>
    public bool Insert(ushort slot, byte index, ushort weapon)
    {
        foreach (var stale in _slots
                     .Where(s => s.Value == weapon && (s.Key.Slot != slot || s.Key.Index != index))
                     .Select(s => s.Key)
                     .ToList())
        {
            _slots.Remove(stale);
        }

        if (_slots.Count >= Capacity && !_slots.ContainsKey((slot, index)))
        {
            return false;
        }

        _slots[(slot, index)] = weapon;

        return true;
    }

    /// <summary>Empties a slot.</summary>
    /// <returns>What was in it, or null when it was already empty.</returns>
    public ushort? Drop(ushort slot, byte index)
        => _slots.Remove((slot, index), out ushort weapon) ? weapon : null;

    /// <summary>
    /// Forgets every slot on a rig that has gone, so nobody is told to holster
    /// something onto a body that is no longer there.
    /// </summary>
    /// <returns>How many were forgotten.</returns>
    public int ForgetSlots(Func<ushort, bool> slotHasGone)
    {
        var doomed = _slots.Keys.Where(k => slotHasGone(k.Slot)).ToList();

        foreach (var key in doomed)
        {
            _slots.Remove(key);
        }

        return doomed.Count;
    }

    /// <summary>
    /// Forgets every slot on a player's body. A rig's slots are keyed by the
    /// player's small id, which no prop id can be.
    /// </summary>
    /// <returns>The weapons that were in them.</returns>
    public IReadOnlyList<ushort> ForgetRig(byte smallId)
    {
        var doomed = _slots.Where(s => s.Key.Slot == smallId).ToList();

        foreach (var slot in doomed)
        {
            _slots.Remove(slot.Key);
        }

        return doomed.Select(s => s.Value).ToList();
    }

    /// <summary>Forgets one slot by name, for a weapon that no longer exists.</summary>
    public bool Forget(ushort slot, byte index) => _slots.Remove((slot, index));

    public void Clear() => _slots.Clear();
}
