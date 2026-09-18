namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A whole message as a Fusion client reads it: NativeMessageHandler.ReadMessage reads a
/// MessagePrefix and then the payload with NetReader.ReadBytes. Trailing bytes are ignored.
/// </summary>
public readonly record struct OracleMessage(byte Tag, byte RelayType, byte Channel, byte? Target, byte? Sender, byte[] Payload)
{
    /// <summary>Null when Fusion would throw reading it.</summary>
    public static OracleMessage? Read(byte[] message)
    {
        try
        {
            var reader = new OracleReader(message);

            byte tag = reader.ReadByte();
            byte relayType = reader.ReadByte();     // MessageRoute, Precision.OneByte
            byte channel = reader.ReadByte();
            byte? target = null;

            if (relayType == 4)
            {
                target = reader.ReadNullableByte();
            }

            if (relayType == 5)
            {
                ReadBytes(reader, message);
            }

            byte? sender = relayType != 0 ? reader.ReadNullableByte() : null;

            return new OracleMessage(tag, relayType, channel, target, sender, ReadBytes(reader, message));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Why a message is not in the one shape Fusion's own writer produces, or null. That shape
    /// is what NetMessage.Create writes: bools as 0 or 1 and a payload length that runs to the end.
    /// A relayed message may carry a null sender, which is what module traffic from mods does.
    /// </summary>
    public static string? NotCanonical(byte[] message)
    {
        try
        {
            var reader = new OracleReader(message);

            reader.ReadByte();
            byte relayType = reader.ReadByte();
            byte channel = reader.ReadByte();

            if (relayType > 5)
            {
                return $"relay type {relayType}";
            }

            if (channel > 1)
            {
                return $"channel {channel}";
            }

            if (relayType == 4)
            {
                byte hasTarget = reader.ReadByte();

                if (hasTarget > 1)
                {
                    return $"target HasValue {hasTarget}";
                }

                if (hasTarget == 1)
                {
                    reader.ReadByte();
                }
            }

            if (relayType == 5)
            {
                int count = reader.ReadInt32();

                if (count is < 0 or > 255 || count > message.Length - reader.Position)
                {
                    return $"target count {count}";
                }

                for (var i = 0; i < count; i++)
                {
                    reader.ReadByte();
                }
            }

            if (relayType != 0)
            {
                byte hasSender = reader.ReadByte();

                if (hasSender > 1)
                {
                    return $"sender HasValue {hasSender}";
                }

                if (hasSender == 1)
                {
                    reader.ReadByte();
                }
            }

            int length = reader.ReadInt32();

            return length == message.Length - reader.Position ? null : $"payload length {length}";
        }
        catch
        {
            return "too short";
        }
    }

    private static byte[] ReadBytes(OracleReader reader, byte[] message)
    {
        int count = reader.ReadInt32();

        if (reader.Position + count > message.Length)
        {
            throw new IndexOutOfRangeException();
        }

        byte[] bytes = new byte[count];
        Array.Copy(message, reader.Position, bytes, 0, count);

        for (var i = 0; i < count; i++)
        {
            reader.ReadByte();
        }

        return bytes;
    }
}
