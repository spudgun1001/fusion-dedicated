using System.Buffers.Binary;
using System.Text;

namespace BonelabServerBrowser.Fusion;

/// <summary>
/// Reads back the big-endian wire format written by <see cref="FusionNetWriter"/>.
/// </summary>
public ref struct FusionNetReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public FusionNetReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public byte ReadByte() => _buffer[_position++];

    public bool ReadBool() => ReadByte() != 0;

    public int ReadInt32()
    {
        int value = BinaryPrimitives.ReadInt32BigEndian(_buffer.Slice(_position, sizeof(int)));
        _position += sizeof(int);
        return value;
    }

    public ulong ReadUInt64()
    {
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(_buffer.Slice(_position, sizeof(ulong)));
        _position += sizeof(ulong);
        return value;
    }

    public byte? ReadNullableByte()
    {
        return ReadBool() ? ReadByte() : null;
    }

    public short ReadInt16()
    {
        short value = BinaryPrimitives.ReadInt16BigEndian(_buffer.Slice(_position, sizeof(short)));
        _position += sizeof(short);
        return value;
    }

    /// <summary>
    /// Fusion stores sbyte as (value + 128), see ByteExtensions.ToSByte.
    /// </summary>
    public sbyte ReadSByte() => (sbyte)(ReadByte() - 128);

    public uint ReadUInt32()
    {
        uint value = BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice(_position, sizeof(uint)));
        _position += sizeof(uint);
        return value;
    }

    public ushort ReadUInt16()
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice(_position, sizeof(ushort)));
        _position += sizeof(ushort);
        return value;
    }

    public float ReadSingle()
    {
        float value = BinaryPrimitives.ReadSingleBigEndian(_buffer.Slice(_position, sizeof(float)));
        _position += sizeof(float);
        return value;
    }

    public ReadOnlySpan<byte> ReadRaw(int count)
    {
        var slice = _buffer.Slice(_position, count);
        _position += count;
        return slice;
    }

    public string? ReadString()
    {
        int byteCount = ReadInt32();

        if (byteCount < 0)
        {
            return null;
        }

        var value = Encoding.UTF8.GetString(_buffer.Slice(_position, byteCount));
        _position += byteCount;
        return value;
    }
}
