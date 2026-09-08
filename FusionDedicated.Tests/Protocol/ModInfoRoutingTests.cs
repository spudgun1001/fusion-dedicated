using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// How a client learns where a mod comes from, and why a dedicated server has to
/// take part in it.
///
/// A client that meets a spawnable or an avatar it has not got asks the entity's
/// owner, never the server. So the server heard none of it, learned nothing, and
/// had nothing to offer the next person. These pin the two halves: the question
/// the server can now ask a player, and the answer coming back to it.
/// </summary>
public class ModInfoRoutingTests
{
    /// <summary>Reads a prefix the way LabFusion's MessagePrefix does.</summary>
    private static (byte Tag, byte RelayType, byte Channel, byte? Target, byte? Sender, byte[] Body)
        ReadAsClient(byte[] message)
    {
        var reader = new OracleReader(message);

        byte tag = reader.ReadByte();
        byte relayType = reader.ReadByte();
        byte channel = reader.ReadByte();

        byte? target = relayType == 4 ? reader.ReadNullableByte() : null;
        byte? sender = relayType != 0 ? reader.ReadNullableByte() : null;

        int length = reader.ReadInt32();
        var body = new byte[length];

        for (int i = 0; i < length; i++)
        {
            body[i] = reader.ReadByte();
        }

        return (tag, relayType, channel, target, sender, body);
    }

    [Fact]
    public void The_server_asks_one_player_and_the_client_sees_itself_as_the_target()
    {
        // ModInfoRequestMessage throws unless Route.Target is its own small id, so
        // a question addressed to nobody in particular is discarded on arrival.
        var message = ServerProtocol.WriteModInfoRequest(7, "Pack.Spawnable.Cruiser", 42);

        var (tag, relayType, channel, target, sender, _) = ReadAsClient(message);

        Assert.Equal(ServerProtocol.TagModInfoRequest, tag);
        Assert.Equal(4, relayType);
        Assert.Equal(0, channel);
        Assert.Equal((byte)7, target);
        Assert.Equal((byte)0, sender);
    }

    [Fact]
    public void The_question_carries_the_barcode_and_the_tracker()
    {
        var message = ServerProtocol.WriteModInfoRequest(7, "Pack.Spawnable.Cruiser", 42);

        var (_, _, _, _, _, body) = ReadAsClient(message);
        var reader = new OracleReader(body);

        Assert.Equal("Pack.Spawnable.Cruiser", reader.ReadString());
        Assert.Equal(42u, reader.ReadUInt32());
    }

    [Fact]
    public void The_server_reads_back_its_own_question()
    {
        var request = ServerProtocol.TryReadModInfoRequest(
            ServerProtocol.WriteModInfoRequest(7, "Pack.Spawnable.Cruiser", 42));

        Assert.NotNull(request);
        Assert.Equal((byte)7, request!.Value.Target);
        Assert.Equal("Pack.Spawnable.Cruiser", request.Value.Barcode);
        Assert.Equal(42u, request.Value.TrackerId);
    }

    [Fact]
    public void The_reply_comes_back_addressed_to_the_server()
    {
        // The client answers whoever asked, which for our question is small id 0,
        // and that is the key the pending question is filed under.
        var reply = ClientModInfoResponse(target: 0, from: 7, modId: 1234, fileId: 99, tracker: 42);

        var response = ServerProtocol.TryReadModInfoResponse(reply);

        Assert.NotNull(response);
        Assert.Equal((byte)0, response!.Value.Target);
        Assert.Equal(1234, response.Value.ModId);
        Assert.Equal(99, response.Value.ModFileId);
        Assert.Equal(42u, response.Value.TrackerId);
    }

    [Fact]
    public void One_client_asking_another_keeps_its_target_through_the_relay()
    {
        // The whole peer path depends on this. Stamping the sender walks past a
        // ToTarget route's target bytes, and getting that wrong sends the question
        // to somebody who does not have the mod, who then returns in silence.
        var asked = ClientModInfoRequest(target: 7, from: 3, "Pack.Spawnable.Cruiser", 42);

        var stamped = ServerProtocol.StampSender(asked, 3);
        var (_, _, _, target, sender, _) = ReadAsClient(stamped);

        Assert.Equal((byte)7, target);
        Assert.Equal((byte)3, sender);
        Assert.Equal((byte)7, ServerProtocol.ReadRoute(stamped).Target);
    }

    [Fact]
    public void A_request_meant_for_a_peer_is_told_apart_from_one_meant_for_the_server()
    {
        var toPeer = ServerProtocol.TryReadModInfoRequest(
            ClientModInfoRequest(target: 7, from: 3, "Pack.Spawnable.Cruiser", 42));

        var toServer = ServerProtocol.TryReadModInfoRequest(
            ClientModInfoRequest(target: 0, from: 3, "Pack.Level.Map", 43));

        Assert.Equal((byte)7, toPeer!.Value.Target);
        Assert.Equal((byte)0, toServer!.Value.Target);
    }

    [Fact]
    public void An_answer_the_server_writes_is_read_back_as_it_wrote_it()
    {
        var reply = ServerProtocol.WriteModInfoResponse(3, 1234, 99, "windows", 42);

        var (_, _, _, target, _, body) = ReadAsClient(reply);
        var reader = new OracleReader(body);

        Assert.Equal((byte)3, target);
        Assert.Equal(1234, reader.ReadInt32());
        Assert.True(reader.ReadBoolean());          // the file id is present
        Assert.Equal(99, reader.ReadInt32());
        Assert.True(reader.ReadBoolean());          // HasFile
        Assert.Equal("windows", reader.ReadString());
        Assert.Equal(42u, reader.ReadUInt32());
    }

    private static byte[] ClientModInfoRequest(byte target, byte from, string barcode, uint tracker)
    {
        var payload = new OracleWriter();
        payload.Write(barcode);
        payload.Write(tracker);

        var message = new OracleWriter();
        message.Write(ServerProtocol.TagModInfoRequest);
        message.Write((byte)4);
        message.Write((byte)0);
        message.Write((byte?)target);
        message.Write((byte?)from);
        message.Write(payload.ToArray());

        return message.ToArray();
    }

    private static byte[] ClientModInfoResponse(byte target, byte from, int modId, int fileId, uint tracker)
    {
        var payload = new OracleWriter();
        payload.Write(modId);
        payload.Write(true);
        payload.Write(fileId);
        payload.Write(true);        // HasFile
        payload.Write("windows");
        payload.Write(tracker);

        var message = new OracleWriter();
        message.Write(ServerProtocol.TagModInfoResponse);
        message.Write((byte)4);
        message.Write((byte)0);
        message.Write((byte?)target);
        message.Write((byte?)from);
        message.Write(payload.ToArray());

        return message.ToArray();
    }
}
