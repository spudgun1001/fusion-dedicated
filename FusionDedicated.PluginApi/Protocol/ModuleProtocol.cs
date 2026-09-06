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
