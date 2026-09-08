using System.Buffers.Binary;

namespace BonelabServerBrowser.Fusion;

/// <summary>
/// Module messages, which is how Fusion carries anything its own modules define
/// rather than the native protocol. The envelope is native tag 200, then an eight
/// byte big-endian handler tag, then that handler's payload.
///
/// The handler tag is a hash of the assembly name and the type's full name. Fusion
/// hashes with its own function rather than the runtime's, which is what makes the
/// tag reproducible here instead of needing a capture to discover it.
/// </summary>
public static class ModuleProtocol
{
    public const byte TagModule = 200;

    private const byte RelayTypeToClients = 2;
    private const byte ChannelReliable = 0;

    /// <summary>Bytes of the handler tag that precede the payload.</summary>
    public const int HandlerTagBytes = 8;

    /// <summary>Point1Id and Point2Id, the last two fields of a constraint.</summary>
    private const int PointIdBytes = 4;

    /// <summary>Fusion's own string hash. Not the runtime's, which varies per run.</summary>
    public static int DeterministicHash(string text)
    {
        unchecked
        {
            int hash1 = (5381 << 16) + 5381;
            int hash2 = hash1;

            for (var i = 0; i < text.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ text[i];

                if (i == text.Length - 1)
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
            }

            return hash1 + (hash2 * 1566083941);
        }
    }

    public static long MakeLong(int left, int right)
    {
        long value = left;
        value <<= 32;
        value |= (uint)right;

        return value;
    }

    public static long TagFor(string assemblyName, string typeName)
        => MakeLong(DeterministicHash(assemblyName), DeterministicHash(typeName));

    /// <summary>
    /// The handler that asks the host to create a constraint. Pinned by a test,
    /// because getting it wrong means the server quietly never sees a constraint.
    /// </summary>
    public static readonly long ConstraintCreateTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.ConstraintCreateMessage");

    /// <summary>
    /// The handler that clears a constraint. Its payload is the two byte ID of
    /// the constraint entity, and nothing else.
    /// </summary>
    public static readonly long ConstraintDeleteTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.ConstraintDeleteMessage");

    /// <summary>
    /// A magazine going into a gun, and coming back out. Payload is the magazine's
    /// entity id then the gun's, two bytes each.
    ///
    /// Read so the server knows a magazine is in a gun rather than lying on the
    /// floor. One in a gun is kinematic, so it sleeps, stops sending poses and
    /// looks abandoned to any clock counting from the last time it moved.
    /// </summary>
    public static readonly long MagazineInsertTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.MagazineInsertMessage");

    /// <summary>Payload is the player, the magazine, the gun, then the hand.</summary>
    public static readonly long MagazineEjectTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.MagazineEjectMessage");

    /// <summary>
    /// Somebody taking hold of a magazine. Owner, entity, hand.
    ///
    /// Read for nothing, and listed so it stays that way. It says who is holding a
    /// magazine, not whether one is in a gun, and Fusion sends it for every
    /// magazine on catch-up including the ones locked into guns. Treating it as a
    /// magazine leaving a gun is what emptied guns two minutes after somebody
    /// joined.
    /// </summary>
    public static readonly long MagazineClaimTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.MagazineClaimMessage");

    /// <summary>
    /// A weapon going into a body slot, which is what holstering is. Payload is
    /// the slot's entity id, the weapon's entity id, then which slot.
    /// </summary>
    public static readonly long InventorySlotInsertTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.InventorySlotInsertMessage");

    /// <summary>
    /// A weapon coming back out of a slot. The payload names the slot rather than
    /// the weapon, so which weapon left has to be remembered from the insert.
    /// </summary>
    public static readonly long InventorySlotDropTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.InventorySlotDropMessage");

    /// <summary>
    /// A magazine taken from the ammo pouch. Also read for nothing.
    ///
    /// The id in it is the pouch, not the magazine, so acting on it marked the
    /// wrong thing entirely.
    /// </summary>
    public static readonly long AmmoReceiverDropTag =
        TagFor("LabFusion", "LabFusion.Marrow.Messages.InventoryAmmoReceiverDropMessage");

    /// <summary>What one of the attachment messages says happened.</summary>
    public enum AttachmentKind
    {
        /// <summary>Not one of them.</summary>
        None,

        /// <summary>Entity is now inside something.</summary>
        Attach,

        /// <summary>Entity is loose again.</summary>
        Detach,

        /// <summary>Entity went into Slot at SlotIndex.</summary>
        SlotInsert,

        /// <summary>Whatever was in Slot at SlotIndex came out. Entity is not named.</summary>
        SlotDrop,
    }

    /// <param name="Holder">The gun a magazine went into. Zero when there is none.</param>
    public readonly record struct AttachmentChange(
        AttachmentKind Kind, ushort Entity, ushort Slot, byte SlotIndex, ushort Holder = 0);

    /// <summary>
    /// Reads one of Fusion's attachment messages.
    ///
    /// The server neither sends nor changes these, it only listens: a magazine in
    /// a gun is kinematic, so it sleeps, stops sending pose updates, and to any
    /// clock counting from the last one it is indistinguishable from a magazine
    /// dropped on the floor an hour ago.
    /// </summary>
    /// <param name="payload">The handler payload, without the eight tag bytes.</param>
    public static AttachmentChange ReadAttachment(long handlerTag, ReadOnlySpan<byte> payload)
    {
        // MagazineInsertData: magazine, gun.
        if (handlerTag == MagazineInsertTag && payload.Length >= 4)
        {
            return new AttachmentChange(
                AttachmentKind.Attach, Id(payload, 0), 0, 0, Id(payload, 2));
        }

        // MagazineEjectData: player, magazine, gun, hand.
        if (handlerTag == MagazineEjectTag && payload.Length >= 6)
        {
            return new AttachmentChange(AttachmentKind.Detach, Id(payload, 1), 0, 0);
        }

        // Nothing else says a magazine left a gun. A claim says who is holding
        // one, which Fusion announces for every magazine on catch-up, guns
        // included, and the ammo pouch message names the pouch rather than the
        // magazine. Both were read as a magazine coming out, and a gun that had
        // been loaded for an hour lost its magazine two minutes after the next
        // person joined.

        // InventorySlotInsertData: slot, weapon, index.
        if (handlerTag == InventorySlotInsertTag && payload.Length >= 5)
        {
            return new AttachmentChange(
                AttachmentKind.SlotInsert, Id(payload, 2), Id(payload, 0), payload[4]);
        }

        // InventorySlotDropData: slot, grabber, index, hand. The weapon that left
        // is not named, so the caller has to remember what the insert put there.
        if (handlerTag == InventorySlotDropTag && payload.Length >= 5)
        {
            return new AttachmentChange(
                AttachmentKind.SlotDrop, 0, Id(payload, 0), payload[3]);
        }

        return default;
    }

    private static ushort Id(ReadOnlySpan<byte> payload, int at)
        => (ushort)((payload[at] << 8) | payload[at + 1]);

    /// <summary>
    /// A magazine sitting in a gun, as MagazineInsertData writes it: the magazine
    /// then the gun.
    ///
    /// Sent to somebody who has just joined. A magazine in a gun is kinematic and
    /// asleep, so it sends no pose updates, and the catch-up puts it wherever it
    /// was last simulated. That is usually mid-air, and it never moves again
    /// because nothing is simulating it.
    /// </summary>
    public static byte[] WriteMagazineInsert(ushort magazine, ushort gun)
    {
        var writer = new FusionNetWriter(8);

        writer.WriteUInt16(magazine);
        writer.WriteUInt16(gun);

        return writer.ToArray();
    }

    /// <summary>
    /// A weapon in a body slot, as InventorySlotInsertData writes it: the slot,
    /// the weapon, then which slot of that receiver.
    ///
    /// The same problem as a magazine. A holstered gun is attached to somebody's
    /// hip and asleep, so a newcomer is told to put it where it was last seen
    /// loose, and it hangs there.
    /// </summary>
    public static byte[] WriteInventorySlotInsert(ushort slot, ushort weapon, byte index)
    {
        var writer = new FusionNetWriter(8);

        writer.WriteUInt16(slot);
        writer.WriteUInt16(weapon);
        writer.Write(index);

        return writer.ToArray();
    }

    /// <summary>Which handler a module message belongs to, or null if it is not one.</summary>
    public static long? TryReadHandlerTag(ReadOnlySpan<byte> message)
    {
        var payload = TryReadPayload(message);

        return payload == null || payload.Value.Length < HandlerTagBytes
            ? null
            : BinaryPrimitives.ReadInt64BigEndian(payload.Value.Span);
    }

    /// <summary>The handler's own bytes, past the tag.</summary>
    public static byte[]? TryReadHandlerPayload(ReadOnlySpan<byte> message)
    {
        var payload = TryReadPayload(message);

        return payload == null || payload.Value.Length < HandlerTagBytes
            ? null
            : payload.Value.Span[HandlerTagBytes..].ToArray();
    }

    private static ReadOnlyMemory<byte>? TryReadPayload(ReadOnlySpan<byte> message)
    {
        try
        {
            if (message.Length < 3 || message[0] != TagModule)
            {
                return null;
            }

            var reader = new FusionNetReader(message);

            reader.ReadByte(); // tag
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            // The route's own fields come before the sender, and skipping them was
            // missing here. A module message aimed at one player then had its
            // handler tag read two bytes early, so the server did not recognise
            // any of them: Fusion sends a constraint catch-up, a destructible and
            // a puppet kill this way.
            if (relayType == 4)
            {
                reader.ReadNullableByte();
            }
            else if (relayType == 5)
            {
                int targets = reader.ReadInt32();

                for (var i = 0; i < targets; i++)
                {
                    reader.ReadByte();
                }
            }

            if (relayType != 0)
            {
                reader.ReadNullableByte();
            }

            int length = reader.ReadInt32();

            return length < 0 ? null : reader.ReadRaw(length).ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the two entity ids a constraint ends with. They are the last fields
    /// written, so nothing before them has to be understood to set them.
    /// </summary>
    public static byte[]? WithPointIds(byte[] handlerPayload, ushort point1, ushort point2)
    {
        if (handlerPayload.Length < PointIdBytes)
        {
            return null;
        }

        var copy = (byte[])handlerPayload.Clone();

        BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(copy.Length - 4, 2), point1);
        BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(copy.Length - 2, 2), point2);

        return copy;
    }

    public static byte[] WriteModuleToClients(long handlerTag, byte senderSmallId, byte[] handlerPayload)
    {
        var payload = new FusionNetWriter(handlerPayload.Length + HandlerTagBytes + 8);

        Span<byte> tag = stackalloc byte[HandlerTagBytes];
        BinaryPrimitives.WriteInt64BigEndian(tag, handlerTag);

        payload.WriteRaw(tag);
        payload.WriteRaw(handlerPayload);

        var message = new FusionNetWriter(handlerPayload.Length + 48);

        message.Write(TagModule);
        message.Write(RelayTypeToClients);
        message.Write(ChannelReliable);
        message.WriteNullable(senderSmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }
}
