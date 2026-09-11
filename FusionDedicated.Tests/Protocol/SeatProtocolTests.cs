using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// PlayerRepSeat, tag 8: which seat a rider got into or out of.
///
/// A client that cannot read one drops it without a word and the rider sits on
/// the hood, so the layout is pinned against the order Fusion's
/// PlayerRepSeatData writes its fields, inside the prefix MessagePrefix writes.
/// </summary>
public class SeatProtocolTests
{
    private const byte Tag = 8;
    private const byte ToOtherClients = 3;
    private const byte ToTarget = 4;
    private const byte Reliable = 0;

    /// <summary>PlayerRepSeatData.Serialize: SeatID, SeatIndex, IsIngress.</summary>
    private static byte[] FusionSeatData(ushort seatId, byte index, bool ingress)
    {
        var data = new OracleWriter();
        data.Write(seatId);
        data.Write(index);
        data.Write(ingress);

        return data.ToArray();
    }

    /// <summary>SeatPatches sending a live seat: ReliableToOtherClients, from the rider.</summary>
    private static byte[] FusionLiveSeat(byte rider, ushort seatId, byte index, bool ingress)
    {
        var message = new OracleWriter();
        message.Write(Tag);
        message.Write(ToOtherClients);
        message.Write(Reliable);
        message.Write((byte?)rider);
        message.Write(FusionSeatData(seatId, index, ingress));

        return message.ToArray();
    }

    /// <summary>SeatExtender's catch-up reply: ToTarget, stamped with whoever answered.</summary>
    private static byte[] FusionCatchupSeat(byte answerer, byte target, ushort seatId, byte index)
    {
        var message = new OracleWriter();
        message.Write(Tag);
        message.Write(ToTarget);
        message.Write(Reliable);
        message.Write((byte?)target);
        message.Write((byte?)answerer);
        message.Write(FusionSeatData(seatId, index, true));

        return message.ToArray();
    }

    [Fact]
    public void Our_seat_message_is_the_bytes_Fusion_would_have_written()
    {
        Assert.Equal(FusionLiveSeat(3, 300, 1, true), FusionProtocol.BuildSeat(3, 300, 1, true));
        Assert.Equal(FusionLiveSeat(7, 65535, 0, false), FusionProtocol.BuildSeat(7, 65535, 0, false));
    }

    [Fact]
    public void A_seat_Fusion_wrote_is_read_field_for_field()
    {
        var read = FusionProtocol.TryReadSeat(FusionLiveSeat(3, 300, 2, true));

        Assert.NotNull(read);
        Assert.Equal(ToOtherClients, read!.Value.RelayType);
        Assert.Equal((ushort)300, read.Value.SeatId);
        Assert.Equal((byte)2, read.Value.Index);
        Assert.True(read.Value.Ingress);
    }

    [Fact]
    public void What_the_server_writes_it_reads_back()
    {
        var random = new Random(20260911);

        for (int i = 0; i < 500; i++)
        {
            byte rider = (byte)random.Next(256);
            ushort seatId = (ushort)random.Next(ushort.MaxValue + 1);
            byte index = (byte)random.Next(256);
            bool ingress = random.Next(2) == 1;

            var read = FusionProtocol.TryReadSeat(FusionProtocol.BuildSeat(rider, seatId, index, ingress));

            Assert.Equal(new FusionProtocol.SeatInfo(ToOtherClients, seatId, index, ingress), read);
        }
    }

    [Fact]
    public void The_built_message_names_the_rider_as_sender()
    {
        // A client seats whoever sent the message, so a replay stamped with anybody
        // else seats the wrong player.
        var reader = new OracleReader(FusionProtocol.BuildSeat(9, 300, 1, true));

        Assert.Equal(Tag, reader.ReadByte());
        Assert.Equal(ToOtherClients, reader.ReadByte());
        Assert.Equal(Reliable, reader.ReadByte());
        Assert.Equal((byte?)9, reader.ReadNullableByte());
    }

    [Fact]
    public void A_catch_up_reply_is_told_apart_from_a_live_seat()
    {
        var reply = FusionProtocol.TryReadSeat(FusionCatchupSeat(answerer: 2, target: 5, seatId: 300, index: 1));
        var live = FusionProtocol.TryReadSeat(FusionLiveSeat(2, 300, 1, true));

        Assert.Equal(ToTarget, reply!.Value.RelayType);
        Assert.Equal(ToOtherClients, live!.Value.RelayType);

        // The target byte is stepped over, so the seat itself still reads true.
        Assert.Equal((ushort)300, reply.Value.SeatId);
        Assert.Equal((byte)1, reply.Value.Index);
        Assert.True(reply.Value.Ingress);
    }

    [Fact]
    public void Another_tag_or_a_cut_short_message_is_not_a_seat()
    {
        Assert.Null(FusionProtocol.TryReadSeat(FusionProtocol.BuildRelease(3, FusionProtocol.Handedness.LEFT)));
        Assert.Null(FusionProtocol.TryReadSeat(new byte[] { 8, 3 }));
        Assert.Null(FusionProtocol.TryReadSeat(ReadOnlySpan<byte>.Empty));
    }
}
