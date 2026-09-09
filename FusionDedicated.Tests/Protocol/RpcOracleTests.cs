using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// What an RPC write looks like on the wire, checked against bytes written out by
/// hand from Fusion's own source rather than from our writer.
///
/// The other RPC tests only prove our reader and our writer agree with each
/// other, which they would do just as happily if both were wrong. These spell
/// out every byte, so a change to either side has to answer for it.
///
/// The layout comes from MessagePrefix.Serialize (Tag, Route, then Sender when
/// the route is not None), MessageRoute.Serialize (type, channel, then the
/// target only for ToTarget), NetWriter.Write(byte?) (a bool flag then the
/// value), and NetWriter.Write(byte[]) (an int32 length then the bytes). Every
/// number is big-endian.
/// </summary>
public class RpcOracleTests
{
    // From Fusion's NativeMessageTag.
    private const byte RpcIntTag = 210;
    private const byte RpcStringTag = 213;
    private const byte RpcBoolTag = 212;

    // From Fusion's RelayType and NetworkChannel.
    private const byte ToTarget = 4;
    private const byte Reliable = 0;

    [Fact]
    public void A_phone_being_told_its_number_is_exactly_these_bytes()
    {
        // Entity 263, variable 1, value 175, sent to and stamped as player 1.
        byte[] path = Convert.FromHexString(RpcProtocol.PathFor(263, 1));
        byte[] body = RpcProtocol.WriteValue(RpcKind.Int, path, RpcValue.OfInt(175));

        byte[] actual = GateProtocol.BuildRpcVariable(RpcIntTag, 1, 1, body);

        byte[] expected =
        {
            210,                    // tag, RPCInt
            ToTarget,               // route type
            Reliable,               // route channel
            1, 1,                   // route target: has value, player 1
            1, 1,                   // sender: has value, player 1
            0, 0, 0, 10,            // body length, ten bytes
            1,                      // path: has entity
            0x01, 0x07,             // entity 263
            0x00, 0x01,             // component index 1
            0,                      // path: no hash follows
            0, 0, 0, 175,           // the number
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void The_path_alone_is_six_bytes_and_reads_back_the_same()
    {
        Assert.Equal("010107000100", RpcProtocol.PathFor(263, 1));

        var read = RpcProtocol.TryReadPath(Convert.FromHexString("010107000100"));

        Assert.NotNull(read);
        Assert.True(read!.Value.HasEntity);
        Assert.Equal(263, read.Value.EntityId);
        Assert.Equal(1, read.Value.ComponentIndex);
    }

    [Fact]
    public void A_bool_is_one_byte_after_the_path()
    {
        byte[] path = Convert.FromHexString(RpcProtocol.PathFor(263, 6));
        byte[] body = RpcProtocol.WriteValue(RpcKind.Bool, path, RpcValue.OfBool(true));

        byte[] actual = GateProtocol.BuildRpcVariable(RpcBoolTag, 2, 2, body);

        byte[] expected =
        {
            212, ToTarget, Reliable, 1, 2, 1, 2,
            0, 0, 0, 7,
            1, 0x01, 0x07, 0x00, 0x06, 0,
            1,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void A_string_is_counted_then_its_utf8()
    {
        byte[] path = Convert.FromHexString(RpcProtocol.PathFor(263, 3));
        byte[] body = RpcProtocol.WriteValue(RpcKind.String, path, RpcValue.OfString("417"));

        byte[] actual = GateProtocol.BuildRpcVariable(RpcStringTag, 1, 1, body);

        byte[] expected =
        {
            213, ToTarget, Reliable, 1, 1, 1, 1,
            0, 0, 0, 13,
            1, 0x01, 0x07, 0x00, 0x03, 0,
            0, 0, 0, 3,             // three characters
            (byte)'4', (byte)'1', (byte)'7',
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void The_prefix_is_the_same_shape_whatever_the_body()
    {
        // Tag, route type, channel, target flag and value, sender flag and value.
        byte[] body = RpcProtocol.WriteValue(
            RpcKind.Int, Convert.FromHexString(RpcProtocol.PathFor(1, 0)), RpcValue.OfInt(0));

        byte[] message = GateProtocol.BuildRpcVariable(RpcIntTag, 9, 3, body);

        Assert.Equal(RpcIntTag, message[0]);
        Assert.Equal(ToTarget, message[1]);
        Assert.Equal(Reliable, message[2]);
        Assert.Equal(1, message[3]);
        Assert.Equal(9, message[4]);
        Assert.Equal(1, message[5]);
        Assert.Equal(3, message[6]);
    }

    [Fact]
    public void What_we_write_is_what_our_own_reader_finds()
    {
        // The weaker check, kept because it catches a reader that drifts.
        byte[] path = Convert.FromHexString(RpcProtocol.PathFor(263, 1));
        byte[] body = RpcProtocol.WriteValue(RpcKind.Int, path, RpcValue.OfInt(175));

        Assert.Equal(path, GateProtocol.TryReadRpcPath(body));
    }
}
