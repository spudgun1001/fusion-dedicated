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
    public void Somebody_holding_a_magazine_is_not_a_magazine_leaving_a_gun()
    {
        // The bug that emptied guns. A claim says who is holding a magazine, and
        // Fusion sends one for every magazine when somebody joins, guns included.
        // Read as a magazine coming out, it started the clock on a gun that had
        // been loaded for an hour, and the magazine went two minutes later.
        var payload = Join(Bytes(7), Id(300), Bytes(1));

        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.MagazineClaimTag, payload);

        Assert.Equal(ModuleProtocol.AttachmentKind.None, change.Kind);
    }

    [Fact]
    public void The_ammo_pouch_message_names_the_pouch_and_is_left_alone()
    {
        // Its id is the receiver, not the magazine, so acting on it marked
        // something else entirely.
        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.AmmoReceiverDropTag, Id(300));

        Assert.Equal(ModuleProtocol.AttachmentKind.None, change.Kind);
    }

    [Fact]
    public void Only_an_eject_takes_a_magazine_out_of_a_gun()
    {
        // Insert in, eject out, and nothing else in between.
        var inserted = ModuleProtocol.ReadAttachment(
            ModuleProtocol.MagazineInsertTag, Join(Id(300), Id(400)));

        var ejected = ModuleProtocol.ReadAttachment(
            ModuleProtocol.MagazineEjectTag, Join(Bytes(7), Id(300), Id(400), Bytes(1)));

        Assert.Equal(ModuleProtocol.AttachmentKind.Attach, inserted.Kind);
        Assert.Equal(ModuleProtocol.AttachmentKind.Detach, ejected.Kind);

        foreach (long other in new[] { ModuleProtocol.MagazineClaimTag, ModuleProtocol.AmmoReceiverDropTag })
        {
            Assert.Equal(ModuleProtocol.AttachmentKind.None,
                ModuleProtocol.ReadAttachment(other, Join(Bytes(7), Id(300), Bytes(1))).Kind);
        }
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

/// <summary>
/// Reading a module message's handler tag when the message is aimed at one player.
///
/// The route's own fields sit between the channel and the sender, and skipping
/// them was missing, so the tag came out two bytes early and matched nothing.
/// Fusion aims four of its module messages this way, including the constraint
/// catch-up it sends a player who has just joined.
/// </summary>
public class ModuleRouteTests
{
    private const long Tag = 0x0102030405060708L;

    private static byte[] Envelope(byte relayType, byte[] routeFields, byte? sender, byte[] body)
    {
        var payload = new OracleWriter();
        payload.Write(Tag);
        payload.Write(body, prefixed: false);

        var message = new OracleWriter();
        message.Write((byte)200);          // Module
        message.Write(relayType);
        message.Write((byte)0);            // channel
        message.Write(routeFields, prefixed: false);

        if (relayType != 0)
        {
            message.Write(sender);
        }

        message.Write(payload.ToArray());

        return message.ToArray();
    }

    [Fact]
    public void A_message_to_everybody_is_read()
    {
        var m = Envelope(2, Array.Empty<byte>(), 3, new byte[] { 9, 9 });

        Assert.Equal(Tag, ModuleProtocol.TryReadHandlerTag(m));
        Assert.Equal(new byte[] { 9, 9 }, ModuleProtocol.TryReadHandlerPayload(m));
    }

    [Fact]
    public void A_message_to_the_server_is_read()
    {
        var m = Envelope(1, Array.Empty<byte>(), 3, new byte[] { 9, 9 });

        Assert.Equal(Tag, ModuleProtocol.TryReadHandlerTag(m));
    }

    [Fact]
    public void A_message_aimed_at_one_player_is_read_past_its_target()
    {
        // ToTarget: a nullable byte sits between the channel and the sender.
        var m = Envelope(4, new byte[] { 1, 7 }, 3, new byte[] { 9, 9 });

        Assert.Equal(Tag, ModuleProtocol.TryReadHandlerTag(m));
        Assert.Equal(new byte[] { 9, 9 }, ModuleProtocol.TryReadHandlerPayload(m));
    }

    [Fact]
    public void A_target_that_is_null_takes_one_byte_rather_than_two()
    {
        var m = Envelope(4, new byte[] { 0 }, 3, new byte[] { 9, 9 });

        Assert.Equal(Tag, ModuleProtocol.TryReadHandlerTag(m));
    }

    [Fact]
    public void A_message_aimed_at_several_players_is_read_past_the_list()
    {
        // ToTargets: an int count then that many small ids.
        var m = Envelope(5, new byte[] { 0, 0, 0, 3, 7, 8, 9 }, 3, new byte[] { 9, 9 });

        Assert.Equal(Tag, ModuleProtocol.TryReadHandlerTag(m));
        Assert.Equal(new byte[] { 9, 9 }, ModuleProtocol.TryReadHandlerPayload(m));
    }

    [Fact]
    public void A_message_with_no_route_has_no_sender_either()
    {
        var m = Envelope(0, Array.Empty<byte>(), null, new byte[] { 9, 9 });

        Assert.Equal(Tag, ModuleProtocol.TryReadHandlerTag(m));
    }
}

/// <summary>
/// Putting a holstered gun back on a hip, and a magazine back in a gun, for
/// somebody who has just joined.
///
/// Both are attached and asleep, so they send no pose updates and the world
/// catch-up can only place them where they were last simulated, which is usually
/// mid-air. Nothing moves them afterwards either, so they hang there. A real host
/// replays this through its own entity catch-up, which runs on the host alone and
/// so never runs on a relay.
/// </summary>
public class ReseatTests
{
    private static (ushort Magazine, ushort Gun) ReadInsert(byte[] payload)
        => ((ushort)((payload[0] << 8) | payload[1]),
            (ushort)((payload[2] << 8) | payload[3]));

    [Fact]
    public void A_magazine_insert_is_written_the_way_the_mod_reads_it()
    {
        // MagazineInsertData: magazine then gun, and getting them the wrong way
        // round seats the gun in the magazine.
        var payload = ModuleProtocol.WriteMagazineInsert(300, 400);

        Assert.Equal(4, payload.Length);
        Assert.Equal((ushort)300, ReadInsert(payload).Magazine);
        Assert.Equal((ushort)400, ReadInsert(payload).Gun);
    }

    [Fact]
    public void What_the_server_writes_is_what_it_reads_back()
    {
        var change = ModuleProtocol.ReadAttachment(
            ModuleProtocol.MagazineInsertTag, ModuleProtocol.WriteMagazineInsert(300, 400));

        Assert.Equal(ModuleProtocol.AttachmentKind.Attach, change.Kind);
        Assert.Equal(300, change.Entity);
        Assert.Equal(400, change.Holder);
    }

    [Fact]
    public void A_holster_insert_is_written_the_way_the_mod_reads_it()
    {
        // InventorySlotInsertData: slot, weapon, index.
        var payload = ModuleProtocol.WriteInventorySlotInsert(500, 400, 3);

        Assert.Equal(5, payload.Length);

        var change = ModuleProtocol.ReadAttachment(ModuleProtocol.InventorySlotInsertTag, payload);

        Assert.Equal(ModuleProtocol.AttachmentKind.SlotInsert, change.Kind);
        Assert.Equal(500, change.Slot);
        Assert.Equal(400, change.Entity);
        Assert.Equal(3, change.SlotIndex);
    }

    [Fact]
    public void An_insert_the_server_sends_is_addressed_to_clients_and_comes_from_it()
    {
        var message = ModuleProtocol.WriteModuleToClients(
            ModuleProtocol.MagazineInsertTag, 0, ModuleProtocol.WriteMagazineInsert(300, 400));

        Assert.Equal(200, message[0]);   // Module
        Assert.Equal(2, message[1]);     // ToClients
        Assert.Equal(ModuleProtocol.MagazineInsertTag, ModuleProtocol.TryReadHandlerTag(message));
        Assert.Equal(new byte[] { 1, 44, 1, 144 }, ModuleProtocol.TryReadHandlerPayload(message));
    }

    [Fact]
    public void The_gun_a_magazine_went_into_is_carried_through_a_real_message()
    {
        // The whole point of tracking the gun: without it the server knows a
        // magazine is in something but not what, so it cannot tell anybody.
        var message = ModuleProtocol.WriteModuleToClients(
            ModuleProtocol.MagazineInsertTag, 0, ModuleProtocol.WriteMagazineInsert(1234, 5678));

        var change = ModuleProtocol.ReadAttachment(
            ModuleProtocol.TryReadHandlerTag(message)!.Value,
            ModuleProtocol.TryReadHandlerPayload(message)!);

        Assert.Equal(1234, change.Entity);
        Assert.Equal(5678, change.Holder);
    }
}
