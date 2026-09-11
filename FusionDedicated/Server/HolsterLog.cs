using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Server;

/// <summary>
/// Detailed log lines about what goes in and out of a slot, so an item on the wrong
/// person's holster can be traced to the message that put it there.
/// </summary>
public static class HolsterLog
{
    /// <param name="item">What went in, or what the server had recorded in the slot for a drop.</param>
    /// <returns>Null for a change that is not a slot, such as a magazine going into a gun.</returns>
    public static string? Change(ModuleProtocol.AttachmentChange change, ushort? item, string itemName,
        string slotOwner, string sender)
        => change.Kind switch
        {
            ModuleProtocol.AttachmentKind.SlotInsert =>
                $"Holster: {sender} put {itemName} (entity {item}) in {slotOwner}'s slot {change.SlotIndex}",
            ModuleProtocol.AttachmentKind.SlotDrop when item != null =>
                $"Holster: {sender} took {itemName} (entity {item}) out of {slotOwner}'s slot {change.SlotIndex}",
            ModuleProtocol.AttachmentKind.SlotDrop =>
                $"Holster: {sender} emptied {slotOwner}'s slot {change.SlotIndex}, which the server had nothing recorded in",
            _ => null,
        };

    public static string Resent(string itemName, ushort item, string slotOwner, byte index, string joiner)
        => $"Holster: told {joiner} that {itemName} (entity {item}) is in {slotOwner}'s slot {index}";

    /// <summary>A player's own slots are keyed by their small id, which no prop id can be.</summary>
    public static string SlotOwner(ushort slot, Func<byte, string?> playerName)
        => slot < EntityRegistry.FirstEntityId
            ? playerName((byte)slot) ?? $"player {slot}"
            : $"entity {slot}";
}
