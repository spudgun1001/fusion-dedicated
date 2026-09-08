using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A magazine inside a gun is kinematic, so Fusion puts it to sleep and it stops
/// sending pose updates. Every clock the server keeps counts from the last pose,
/// so from the outside it is identical to one dropped on the floor an hour ago.
/// These messages are the only thing that tells them apart.
///
/// The layouts are LabFusion's own, and one wrong offset reads a hand or a slot
/// index as an entity id, so each is pinned field by field.
/// </summary>
public class AttachmentTests
{
    private static byte[] Bytes(params int[] values)
        => values.Select(v => (byte)v).ToArray();

    private static byte[] Id(ushort id) => new[] { (byte)(id >> 8), (byte)(id & 0xFF) };

    private static byte[] Join(params byte[][] parts)
        => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void A_magazine_going_into_a_gun_names_the_magazine_not_the_gun()
    {
        // MagazineInsertData: magazine, gun.
        var payload = Join(Id(300), Id(400));

        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.MagazineInsertTag, payload);

        Assert.Equal(ModuleProtocol.AttachmentKind.Attach, change.Kind);
        Assert.Equal(300, change.Entity);
    }

    [Fact]
    public void A_magazine_coming_out_is_read_past_the_player_that_pulled_it()
    {
        // MagazineEjectData: player, magazine, gun, hand.
        var payload = Join(Bytes(7), Id(300), Id(400), Bytes(1));

        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.MagazineEjectTag, payload);

        Assert.Equal(ModuleProtocol.AttachmentKind.Detach, change.Kind);
        Assert.Equal(300, change.Entity);
    }

    [Fact]
    public void A_magazine_taken_in_hand_is_no_longer_in_a_gun()
    {
        // MagazineClaimData: owner, entity, hand.
        var payload = Join(Bytes(7), Id(300), Bytes(1));

        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.MagazineClaimTag, payload);

        Assert.Equal(ModuleProtocol.AttachmentKind.Detach, change.Kind);
        Assert.Equal(300, change.Entity);
    }

    [Fact]
    public void A_magazine_out_of_the_ammo_pouch_is_loose()
    {
        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.AmmoReceiverDropTag, Id(300));

        Assert.Equal(ModuleProtocol.AttachmentKind.Detach, change.Kind);
        Assert.Equal(300, change.Entity);
    }

    [Fact]
    public void Holstering_names_the_weapon_the_slot_and_which_slot()
    {
        // InventorySlotInsertData: slot, weapon, index.
        var payload = Join(Id(500), Id(400), Bytes(3));

        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.InventorySlotInsertTag, payload);

        Assert.Equal(ModuleProtocol.AttachmentKind.SlotInsert, change.Kind);
        Assert.Equal(400, change.Entity);
        Assert.Equal(500, change.Slot);
        Assert.Equal(3, change.SlotIndex);
    }

    [Fact]
    public void Unholstering_names_the_slot_but_not_the_weapon()
    {
        // InventorySlotDropData: slot, grabber, index, hand. The index sits after
        // the grabber, so reading it one byte early gives a player id instead.
        var payload = Join(Id(500), Bytes(7), Bytes(3), Bytes(1));

        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.InventorySlotDropTag, payload);

        Assert.Equal(ModuleProtocol.AttachmentKind.SlotDrop, change.Kind);
        Assert.Equal(500, change.Slot);
        Assert.Equal(3, change.SlotIndex);
    }

    [Fact]
    public void Anything_else_is_left_alone()
    {
        Assert.Equal(ModuleProtocol.AttachmentKind.None,
            ModuleProtocol.ReadAttachment(ModuleProtocol.ConstraintCreateTag, Id(300)).Kind);
    }

    [Fact]
    public void A_payload_too_short_to_hold_the_fields_is_refused()
    {
        // A truncated message must not read past its own end.
        Assert.Equal(ModuleProtocol.AttachmentKind.None,
            ModuleProtocol.ReadAttachment(ModuleProtocol.MagazineInsertTag, new byte[] { 1 }).Kind);

        Assert.Equal(ModuleProtocol.AttachmentKind.None,
            ModuleProtocol.ReadAttachment(ModuleProtocol.InventorySlotDropTag, new byte[] { 1, 2 }).Kind);
    }

    [Fact]
    public void Every_handler_has_its_own_tag()
    {
        var tags = new[]
        {
            ModuleProtocol.MagazineInsertTag,
            ModuleProtocol.MagazineEjectTag,
            ModuleProtocol.MagazineClaimTag,
            ModuleProtocol.InventorySlotInsertTag,
            ModuleProtocol.InventorySlotDropTag,
            ModuleProtocol.AmmoReceiverDropTag,
            ModuleProtocol.ConstraintCreateTag,
            ModuleProtocol.ConstraintDeleteTag,
        };

        Assert.Equal(tags.Length, tags.Distinct().Count());
    }

    [Fact]
    public void The_tags_are_the_ones_LabFusion_computes()
    {
        // Fusion hashes the assembly name and the full type name. A typo in either
        // gives a tag that matches nothing, and the server then quietly never sees
        // the message, which is a fault with no symptom until a gun empties itself.
        Assert.Equal(
            ModuleProtocol.TagFor("LabFusion", "LabFusion.Marrow.Messages.MagazineInsertMessage"),
            ModuleProtocol.MagazineInsertTag);

        Assert.Equal(
            ModuleProtocol.TagFor("LabFusion", "LabFusion.Marrow.Messages.InventorySlotDropMessage"),
            ModuleProtocol.InventorySlotDropTag);
    }
}
