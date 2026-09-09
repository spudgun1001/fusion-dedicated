using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A whole RPC message, from a client and back out again.
///
/// The pieces are tested separately; this is the seam between them, where the
/// prefix is skipped to find the body and the body is put back inside a new
/// prefix. Getting either wrong means a plugin reads a path that is really half a
/// length field, or sends one a client throws away without a word.
/// </summary>
public class RpcMessageTests
{
    private const byte RpcString = 213;
    private const byte RpcEvent = 209;

    /// <summary>An RPCString as a client sends it: ToOtherClients, sender stamped.</summary>
    private static byte[] FromClient(byte tag, byte sender, byte[] payload)
    {
        var message = new FusionNetWriter(payload.Length + 32);

        message.Write(tag);
        message.Write((byte)3);           // ToOtherClients
        message.Write((byte)0);           // reliable
        message.WriteNullable(sender);
        message.WriteBlock(payload);

        return message.ToArray();
    }

    private static byte[] Path() => new byte[] { 1, 1, 44, 0, 2, 0 };

    [Fact]
    public void The_body_of_a_clients_rpc_is_found_past_the_prefix()
    {
        var payload = RpcProtocol.WriteValue(RpcKind.String, Path(), RpcValue.OfString("417"));

        var body = GateProtocol.TryReadBody(FromClient(RpcString, 3, payload), RpcString);

        Assert.NotNull(body);
        Assert.Equal(payload, body);
    }

    [Fact]
    public void The_component_and_the_value_come_back_out_of_a_real_message()
    {
        var payload = RpcProtocol.WriteValue(RpcKind.String, Path(), RpcValue.OfString("call-7f31a2"));
        var body = GateProtocol.TryReadBody(FromClient(RpcString, 3, payload), RpcString)!;

        var path = RpcProtocol.TryReadPath(body);

        Assert.Equal((ushort)300, path!.Value.EntityId);
        Assert.Equal("call-7f31a2", RpcProtocol.ReadValue(RpcKind.String, body).Text);
    }

    [Fact]
    public void An_event_carries_a_component_and_no_value()
    {
        var payload = RpcProtocol.WriteValue(RpcKind.Event, Path(), RpcValue.Nothing);
        var body = GateProtocol.TryReadBody(FromClient(RpcEvent, 3, payload), RpcEvent)!;

        Assert.Equal((ushort)300, RpcProtocol.TryReadPath(body)!.Value.EntityId);
    }

    [Fact]
    public void What_the_server_sends_is_aimed_at_one_player_and_reads_back()
    {
        // Nothing on the receiving side checks who sent an RPC, but the route has
        // to be right or it reaches nobody.
        var payload = RpcProtocol.WriteValue(RpcKind.String, Path(), RpcValue.OfString("417"));
        var message = GateProtocol.BuildRpcVariable(RpcString, 7, 0, payload);

        var reader = new OracleReader(message);

        Assert.Equal(RpcString, reader.ReadByte());
        Assert.Equal(4, reader.ReadByte());               // ToTarget
        Assert.Equal(0, reader.ReadByte());               // reliable
        Assert.Equal((byte)7, reader.ReadNullableByte()); // the player
        Assert.Equal((byte)0, reader.ReadNullableByte()); // the server
        Assert.Equal(payload.Length, reader.ReadInt32());
    }

    [Fact]
    public void A_message_the_server_built_is_read_by_the_same_code_that_reads_a_clients()
    {
        var payload = RpcProtocol.WriteValue(RpcKind.Int, Path(), RpcValue.OfInt(417));
        var message = GateProtocol.BuildRpcVariable(210, 7, 0, payload);

        var body = GateProtocol.TryReadBody(message, 210);

        Assert.Equal(417, RpcProtocol.ReadValue(RpcKind.Int, body!).Int);
    }

    [Fact]
    public void A_path_the_server_was_handed_as_text_survives_the_trip_back()
    {
        // A plugin holds a path as hex, because that is what it can put in a
        // dictionary. It has to come back out as the same bytes.
        string key = RpcProtocol.TryReadPath(Path())!.Value.Key;

        var payload = RpcProtocol.WriteValue(
            RpcKind.String, Convert.FromHexString(key), RpcValue.OfString("417"));

        Assert.Equal(key, RpcProtocol.TryReadPath(payload)!.Value.Key);
    }
}
