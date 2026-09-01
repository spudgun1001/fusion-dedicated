using System.Buffers.Binary;
using System.Text;

namespace FusionDedicated.Commands.Rcon;

public static class RconPacketType
{
    public const int Response = 0;

    /// <summary>Inbound this means EXECCOMMAND; outbound it means AUTH_RESPONSE.</summary>
    public const int ExecCommandOrAuthResponse = 2;

    public const int Auth = 3;
}

public readonly record struct RconPacket(int Id, int Type, string Body);

/// <summary>
/// Source RCON framing: little-endian size, id and type, then a null-terminated
/// body and one more null byte. Size counts everything after itself.
/// </summary>
public static class RconCodec
{
    /// <summary>Id, type and the two terminating nulls.</summary>
    private const int Overhead = 10;

    /// <summary>Valve's documented ceiling.</summary>
    public const int MaxPacketSize = 4096;

    public static byte[] Encode(RconPacket packet)
    {
        var body = Encoding.UTF8.GetBytes(packet.Body);
        var buffer = new byte[4 + Overhead + body.Length];

        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), Overhead + body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), packet.Id);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), packet.Type);

        body.CopyTo(buffer.AsSpan(12));

        return buffer;
    }

    /// <summary>
    /// Total bytes this packet occupies, or -1 when the size field has not arrived.
    /// Throws for a hostile size, so a client cannot force a huge allocation.
    /// </summary>
    public static int RequiredLength(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 4)
        {
            return -1;
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(buffer);

        if (size < Overhead || size > MaxPacketSize)
        {
            throw new InvalidDataException($"RCON packet size {size} is out of range.");
        }

        return size + 4;
    }

    public static RconPacket Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 4 + Overhead)
        {
            throw new InvalidDataException("RCON packet is too short.");
        }

        int size = BinaryPrimitives.ReadInt32LittleEndian(packet);
        int id = BinaryPrimitives.ReadInt32LittleEndian(packet[4..]);
        int type = BinaryPrimitives.ReadInt32LittleEndian(packet[8..]);

        int bodyLength = size - Overhead;

        if (bodyLength < 0 || 12 + bodyLength > packet.Length)
        {
            throw new InvalidDataException("RCON packet body runs past the buffer.");
        }

        return new RconPacket(id, type, Encoding.UTF8.GetString(packet.Slice(12, bodyLength)));
    }
}
