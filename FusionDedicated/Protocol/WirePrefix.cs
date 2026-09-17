using System.Buffers.Binary;

namespace FusionDedicated.Protocol;

/// <summary>
/// The one prefix shape Fusion's NetMessage.Create writes. A client reads a message with
/// NetReader, where a bool is true only when it is 1 and the payload is length prefixed, so
/// any other shape could show clients different bytes from the ones the server checked.
/// </summary>
public static class WirePrefix
{
    /// <summary>Why the prefix is not canonical, or null when it is.</summary>
    public static string? Problem(ReadOnlySpan<byte> message)
    {
        if (message.Length < 3)
        {
            return "too short for a prefix";
        }

        byte relayType = message[1];
        byte channel = message[2];
        int position = 3;

        // RelayType and NetworkChannel, each one byte.
        if (relayType > 5)
        {
            return $"relay type {relayType}";
        }

        if (channel > 1)
        {
            return $"channel {channel}";
        }

        // MessageRoute: ToTarget carries a nullable byte, ToTargets a length prefixed byte array.
        if (relayType == 4)
        {
            if (position >= message.Length || message[position] > 1)
            {
                return position >= message.Length ? "too short for a target" : $"target HasValue {message[position]}";
            }

            position += message[position] == 1 ? 2 : 1;
        }
        else if (relayType == 5)
        {
            if (position + 4 > message.Length)
            {
                return "too short for a target list";
            }

            int count = BinaryPrimitives.ReadInt32BigEndian(message.Slice(position, 4));
            position += 4;

            if (count is < 0 or > 255 || count > message.Length - position)
            {
                return $"target count {count}";
            }

            position += count;
        }

        // MessagePrefix: MessageRelay always writes the local small id on a relayed route.
        if (relayType != 0)
        {
            if (position + 2 > message.Length || message[position] != 1)
            {
                return position >= message.Length ? "too short for a sender" : $"sender HasValue {message[position]}";
            }

            position += 2;
        }

        if (position + 4 > message.Length)
        {
            return "too short for a payload length";
        }

        int length = BinaryPrimitives.ReadInt32BigEndian(message.Slice(position, 4));
        position += 4;

        return length == message.Length - position
            ? null
            : $"payload length {length} with {message.Length - position} bytes left";
    }
}
