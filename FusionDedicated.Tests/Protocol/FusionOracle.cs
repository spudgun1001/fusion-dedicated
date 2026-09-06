using System.Buffers.Binary;
using System.Text;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Fusion's own NetWriter and NetReader, transcribed for use as a test oracle.
///
/// Taken line for line from the decompiled LabFusion assembly, at
/// LabFusion.Network.Serialization.NetWriter and .NetReader, with the array
/// pooling and the bounds messages left out because a test does not need them.
/// Nothing here is clever on purpose: the point is that it matches Fusion rather
/// than that it is good, so our own writer can be checked against something we
/// did not design.
///
/// The sbyte conversion is Fusion's ByteExtensions.ToByte, which is
/// (value + 128) rather than a reinterpret cast, and it is the sort of detail
/// this file exists to pin.
/// </summary>
public sealed class OracleWriter
{
    private readonly byte[] _buffer = new byte[65536];

    public int Position { get; private set; }

    public void Write(byte value) => _buffer[Position++] = value;

    public void Write(bool value) => Write(value ? (byte)1 : (byte)0);

    public void Write(short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(new Span<byte>(_buffer, Position, 2), value);
        Position += 2;
    }

    public void Write(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(_buffer, Position, 4), value);
        Position += 4;
    }

    public void Write(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(new Span<byte>(_buffer, Position, 8), value);
        Position += 8;
    }

    public void Write(double value)
    {
        BinaryPrimitives.WriteDoubleBigEndian(new Span<byte>(_buffer, Position, 8), value);
        Position += 8;
    }

    public void Write(sbyte value) => Write((byte)(value + 128));

    public void Write(float value)
    {
        BinaryPrimitives.WriteSingleBigEndian(new Span<byte>(_buffer, Position, 4), value);
        Position += 4;
    }

    public void Write(ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(_buffer, Position, 2), value);
        Position += 2;
    }

    public void Write(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(_buffer, Position, 4), value);
        Position += 4;
    }

    public void Write(ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(new Span<byte>(_buffer, Position, 8), value);
        Position += 8;
    }

    public void Write(string? value)
    {
        if (value == null)
        {
            Write(-1);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        Write(byteCount);
        Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, Position);
        Position += byteCount;
    }

    /// <summary>
    /// Fusion's Write(byte[]) puts an int length in front. NetMessage appends a
    /// module body that way, while ModuleMessageManager writes the handler tag and
    /// the handler's data with no prefix at all, so both are needed here.
    /// </summary>
    public void Write(byte[] value, bool prefixed = true)
    {
        if (prefixed)
        {
            Write(value.Length);
        }

        Buffer.BlockCopy(value, 0, _buffer, Position, value.Length);
        Position += value.Length;
    }

    public void Write(byte? value)
    {
        Write(value.HasValue);

        if (value.HasValue)
        {
            Write(value.Value);
        }
    }

    /// <summary>NetSerializerExtensions.SerializeValue for a Dictionary.</summary>
    public void Write(Dictionary<string, string> value)
    {
        Write(value.Count);

        foreach (var pair in value)
        {
            Write(pair.Key);
            Write(pair.Value);
        }
    }

    /// <summary>NetSerializerExtensions.SerializeValue for a List.</summary>
    public void Write(List<string> value)
    {
        Write(value.Count);

        foreach (string item in value)
        {
            Write(item);
        }
    }

    public byte[] ToArray() => _buffer.AsSpan(0, Position).ToArray();
}

/// <summary>Fusion's NetReader, transcribed the same way.</summary>
public sealed class OracleReader
{
    private readonly byte[] _buffer;

    public OracleReader(byte[] buffer) => _buffer = buffer;

    public int Position { get; private set; }

    public byte ReadByte() => _buffer[Position++];

    public bool ReadBoolean() => ReadByte() == 1;

    public short ReadInt16()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(new ReadOnlySpan<byte>(_buffer, Position, 2));
        Position += 2;

        return value;
    }

    public int ReadInt32()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(_buffer, Position, 4));
        Position += 4;

        return value;
    }

    public long ReadInt64()
    {
        long value = BinaryPrimitives.ReadInt64BigEndian(new ReadOnlySpan<byte>(_buffer, Position, 8));
        Position += 8;

        return value;
    }

    public sbyte ReadSByte() => (sbyte)(ReadByte() - 128);

    public float ReadSingle()
    {
        float value = BinaryPrimitives.ReadSingleBigEndian(new ReadOnlySpan<byte>(_buffer, Position, 4));
        Position += 4;

        return value;
    }

    public ushort ReadUInt16()
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(_buffer, Position, 2));
        Position += 2;

        return value;
    }

    public uint ReadUInt32()
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(_buffer, Position, 4));
        Position += 4;

        return value;
    }

    public ulong ReadUInt64()
    {
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(_buffer, Position, 8));
        Position += 8;

        return value;
    }

    public string? ReadString()
    {
        int count = ReadInt32();

        if (count <= -1)
        {
            return null;
        }

        string value = Encoding.UTF8.GetString(_buffer, Position, count);
        Position += count;

        return value;
    }

    public byte? ReadNullableByte() => ReadBoolean() ? ReadByte() : null;
}
