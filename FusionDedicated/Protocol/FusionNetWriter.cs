using System.Buffers.Binary;
using System.Text;

namespace BonelabServerBrowser.Fusion;

/// <summary>
/// Minimal re-implementation of Fusion's NetWriter wire format.
/// Every multi-byte value is written big-endian, strings are length-prefixed UTF-8.
/// </summary>
public sealed class FusionNetWriter
{
    private byte[] _buffer;

    public int Position { get; private set; }

    public FusionNetWriter(int capacity = 4096)
    {
        _buffer = new byte[capacity];
        Position = 0;
    }

    private void EnsureCapacity(int extra)
    {
        if (Position + extra <= _buffer.Length)
        {
            return;
        }

        var grown = new byte[Math.Max(_buffer.Length * 2, Position + extra)];
        Array.Copy(_buffer, grown, Position);
        _buffer = grown;
    }

    public void Write(byte value)
    {
        EnsureCapacity(1);
        _buffer[Position++] = value;
    }

    public void Write(bool value) => Write((byte)(value ? 1 : 0));

    public void Write(int value)
    {
        EnsureCapacity(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(_buffer, Position, sizeof(int)), value);
        Position += sizeof(int);
    }

    public void WriteInt16(short value)
    {
        EnsureCapacity(sizeof(short));
        BinaryPrimitives.WriteInt16BigEndian(new Span<byte>(_buffer, Position, sizeof(short)), value);
        Position += sizeof(short);
    }

    /// <summary>
    /// Fusion writes sbyte as (value + 128), see ByteExtensions.ToByte.
    /// </summary>
    public void WriteSByte(sbyte value) => Write((byte)(value + 128));

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(new Span<byte>(_buffer, Position, sizeof(uint)), value);
        Position += sizeof(uint);
    }

    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(_buffer, Position, sizeof(ushort)), value);
        Position += sizeof(ushort);
    }

    public void Write(ulong value)
    {
        EnsureCapacity(sizeof(ulong));
        BinaryPrimitives.WriteUInt64BigEndian(new Span<byte>(_buffer, Position, sizeof(ulong)), value);
        Position += sizeof(ulong);
    }

    public void Write(float value)
    {
        EnsureCapacity(sizeof(float));
        BinaryPrimitives.WriteSingleBigEndian(new Span<byte>(_buffer, Position, sizeof(float)), value);
        Position += sizeof(float);
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

        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, Position);
        Position += byteCount;
    }

    /// <summary>
    /// Writes a nullable byte the way Fusion does: a presence flag, then the value.
    /// </summary>
    public void WriteNullable(byte? value)
    {
        Write(value.HasValue);

        if (value.HasValue)
        {
            Write(value.Value);
        }
    }

    /// <summary>
    /// Writes a length-prefixed raw byte block (Fusion's Write(byte[]) / Write(ArraySegment)).
    /// </summary>
    public void WriteBlock(ReadOnlySpan<byte> value)
    {
        Write(value.Length);
        EnsureCapacity(value.Length);
        value.CopyTo(new Span<byte>(_buffer, Position, value.Length));
        Position += value.Length;
    }

    /// <summary>
    /// Writes bytes verbatim, with no length prefix.
    /// </summary>
    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(new Span<byte>(_buffer, Position, value.Length));
        Position += value.Length;
    }

    public void Write(Dictionary<string, string> value)
    {
        Write(value.Count);

        foreach (var pair in value)
        {
            Write(pair.Key);
            Write(pair.Value);
        }
    }

    public void Write(List<string> value)
    {
        Write(value.Count);

        foreach (var item in value)
        {
            Write(item);
        }
    }

    public byte[] ToArray() => new ReadOnlySpan<byte>(_buffer, 0, Position).ToArray();
}
