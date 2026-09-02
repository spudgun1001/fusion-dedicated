using System.Buffers.Binary;
using System.Text;
using FusionDedicated.Commands.Rcon;

namespace FusionDedicated.Tests.Commands;

public class RconPacketTests
{
    [Fact]
    public void Encode_lays_out_size_id_type_body_and_two_nulls()
    {
        var bytes = RconCodec.Encode(new RconPacket(7, 2, "hi"));

        Assert.Equal(4 + 10 + 2, bytes.Length);
        Assert.Equal(12, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)));
        Assert.Equal("hi", Encoding.UTF8.GetString(bytes.AsSpan(12, 2)));
        Assert.Equal(0, bytes[^1]);
        Assert.Equal(0, bytes[^2]);
    }

    [Fact]
    public void Encode_then_decode_round_trips()
    {
        var original = new RconPacket(42, 3, "hunter2");

        var decoded = RconCodec.Decode(RconCodec.Encode(original));

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void An_empty_body_round_trips()
    {
        Assert.Equal("", RconCodec.Decode(RconCodec.Encode(new RconPacket(1, 0, ""))).Body);
    }

    [Fact]
    public void RequiredLength_reports_the_whole_packet_size()
    {
        var bytes = RconCodec.Encode(new RconPacket(1, 2, "abc"));

        Assert.Equal(bytes.Length, RconCodec.RequiredLength(bytes));
    }

    [Fact]
    public void RequiredLength_is_negative_until_the_size_field_arrives()
    {
        Assert.Equal(-1, RconCodec.RequiredLength(new byte[3]));
    }

    [Fact]
    public void RequiredLength_still_reports_the_size_from_a_partial_packet()
    {
        var bytes = RconCodec.Encode(new RconPacket(1, 2, "a longer body here"));

        Assert.Equal(bytes.Length, RconCodec.RequiredLength(bytes.AsSpan(0, 6)));
    }

    [Fact]
    public void An_absurd_size_is_rejected_rather_than_allocating()
    {
        var hostile = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(hostile, int.MaxValue);

        Assert.Throws<InvalidDataException>(() => RconCodec.RequiredLength(hostile));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void A_size_below_the_minimum_is_rejected(int size)
    {
        var hostile = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(hostile, size);

        Assert.Throws<InvalidDataException>(() => RconCodec.RequiredLength(hostile));
    }

    [Fact]
    public void Decode_rejects_a_truncated_packet()
    {
        Assert.Throws<InvalidDataException>(() => RconCodec.Decode(new byte[8]));
    }

    [Fact]
    public void Utf8_bodies_survive_the_round_trip()
    {
        var decoded = RconCodec.Decode(RconCodec.Encode(new RconPacket(1, 2, "café, ok")));

        Assert.Equal("café, ok", decoded.Body);
    }
}
