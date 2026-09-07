using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A scene object is not networked until somebody interacts with it. The client
/// builds the entity, parks it under a temporary id and asks the server for a
/// real one, and only the host answers. On a relay nothing did.
///
/// Everything downstream waits with it: the registration callback never fires,
/// NetworkPropCreate is never sent, and nobody else hears the object exists.
/// That is the destructible door that works alone and not together, and the
/// passenger who never looks seated, because SeatPatches awaits this same reply.
/// </summary>
public class UnqueueTests
{
    /// <summary>An EntityUnqueueRequest as Fusion's own serializer writes one.</summary>
    private static byte[] FusionRequest(byte userId, ushort queuedId)
    {
        var data = new OracleWriter();
        data.Write(userId);
        data.Write(queuedId);

        var message = new OracleWriter();
        message.Write((byte)13);        // EntityUnqueueRequest
        message.Write((byte)1);         // ToServer
        message.Write((byte)0);         // Reliable
        message.Write((byte?)userId);   // sender
        message.Write(data.ToArray());

        return message.ToArray();
    }

    [Fact]
    public void A_request_written_by_Fusion_reads_back()
    {
        var request = FusionProtocol.TryReadUnqueueRequest(FusionRequest(3, 4242));

        Assert.NotNull(request);
        Assert.Equal(3, request!.Value.UserSmallId);
        Assert.Equal(4242, request.Value.QueuedId);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 65535)]
    [InlineData(1, 256)]
    public void Every_shape_of_request_reads_back(byte user, ushort queued)
    {
        var request = FusionProtocol.TryReadUnqueueRequest(FusionRequest(user, queued));

        Assert.Equal(user, request!.Value.UserSmallId);
        Assert.Equal(queued, request.Value.QueuedId);
    }

    [Fact]
    public void Something_that_is_not_a_request_is_refused_rather_than_throwing()
    {
        Assert.Null(FusionProtocol.TryReadUnqueueRequest(new byte[] { 13 }));
        Assert.Null(FusionProtocol.TryReadUnqueueRequest(Array.Empty<byte>()));
    }

    [Fact]
    public void Our_answer_is_the_bytes_Fusion_would_have_written()
    {
        // MessagePrefix: tag, relay, channel, then the route's target because it
        // is ToTarget, then the sender. Both are nullable bytes, so each is a
        // presence flag and a value. Then the body as a length prefixed block.
        var data = new OracleWriter();
        data.Write((ushort)4242);   // queuedId
        data.Write((ushort)300);    // allocatedId

        var expected = new OracleWriter();
        expected.Write((byte)14);       // EntityUnqueueResponse
        expected.Write((byte)4);        // ToTarget
        expected.Write((byte)0);        // Reliable
        expected.Write((byte?)3);       // the route's target
        expected.Write((byte?)0);       // sender: the server
        expected.Write(data.ToArray());

        Assert.Equal(expected.ToArray(), FusionProtocol.BuildUnqueueResponse(3, 4242, 300));
    }

    [Fact]
    public void The_answer_is_addressed_to_the_one_who_asked()
    {
        byte[] message = FusionProtocol.BuildUnqueueResponse(7, 1, 2);
        var route = ServerProtocol.ReadRoute(message);

        Assert.Equal(4, route.RelayType);
        Assert.Equal((byte?)7, route.Target);
    }

    [Fact]
    public void The_answer_carries_the_queued_id_back_so_the_client_can_match_it()
    {
        byte[] message = FusionProtocol.BuildUnqueueResponse(3, 4242, 300);
        var fusion = new OracleReader(message);

        fusion.ReadByte();              // tag
        fusion.ReadByte();              // relay
        fusion.ReadByte();              // channel
        fusion.ReadNullableByte();      // target
        fusion.ReadNullableByte();      // sender
        fusion.ReadInt32();             // length

        Assert.Equal(4242, fusion.ReadUInt16());
        Assert.Equal(300, fusion.ReadUInt16());
    }

    [Fact]
    public void A_request_and_our_answer_agree_on_the_queued_id()
    {
        // The client matches the reply to its own pending entity by this number.
        // Getting it wrong leaves the entity queued exactly as before.
        var request = FusionProtocol.TryReadUnqueueRequest(FusionRequest(5, 9001))!.Value;

        byte[] answer = FusionProtocol.BuildUnqueueResponse(
            request.UserSmallId, request.QueuedId, 400);

        var fusion = new OracleReader(answer);

        for (int i = 0; i < 3; i++) { fusion.ReadByte(); }

        Assert.Equal((byte)5, fusion.ReadNullableByte());
        fusion.ReadNullableByte();
        fusion.ReadInt32();

        Assert.Equal(9001, fusion.ReadUInt16());
    }

    [Fact]
    public void The_prop_create_that_follows_reads_back()
    {
        // After the unqueue reply the client broadcasts this, naming the object
        // inside the level. Keeping it is what lets a later joiner bind their own
        // copy to the same id instead of minting a second one.
        var data = new OracleWriter();
        data.Write((byte)3);        // owner
        data.Write(1234567);        // hash
        data.Write(2);              // index
        data.Write((ushort)300);    // entity id

        var message = new OracleWriter();
        message.Write((byte)18);
        message.Write((byte)3);     // ToOtherClients
        message.Write((byte)0);
        message.Write((byte?)3);
        message.Write(data.ToArray());

        var prop = FusionProtocol.TryReadPropCreate(message.ToArray());

        Assert.NotNull(prop);
        Assert.Equal(3, prop!.Value.OwnerSmallId);
        Assert.Equal(1234567, prop.Value.Hash);
        Assert.Equal(2, prop.Value.Index);
        Assert.Equal(300, prop.Value.EntityId);
    }

    [Fact]
    public void Our_prop_create_is_the_bytes_Fusion_would_have_written()
    {
        var data = new OracleWriter();
        data.Write((byte)3);
        data.Write(1234567);
        data.Write(2);
        data.Write((ushort)300);

        var expected = new OracleWriter();
        expected.Write((byte)18);
        expected.Write((byte)3);
        expected.Write((byte)0);
        expected.Write((byte?)3);
        expected.Write(data.ToArray());

        Assert.Equal(expected.ToArray(), FusionProtocol.BuildPropCreate(3, 1234567, 2, 300));
    }

    [Fact]
    public void A_prop_create_round_trips_through_us_unchanged()
    {
        // What the replay does: read one off the wire, keep it, write it back.
        var prop = FusionProtocol.TryReadPropCreate(
            FusionProtocol.BuildPropCreate(5, -99, 7, 4242))!.Value;

        Assert.Equal(5, prop.OwnerSmallId);
        Assert.Equal(-99, prop.Hash);
        Assert.Equal(7, prop.Index);
        Assert.Equal(4242, prop.EntityId);
    }

    [Fact]
    public void A_spawn_carries_a_source_Fusion_recognises()
    {
        // EntitySource is None, Scene, Player. We were sending three, which is
        // not a member of it, and a client comparing against Player took the
        // wrong branch: magazines were never cleaned up and piled up instead.
        byte[] message = FusionProtocol.BuildSpawnResponse(
            1, 1, 300, "Pack.Spawnable.Mag", new Vec3(0, 0, 0), null, 0);

        Assert.InRange(message[^1], FusionProtocol.SourceNone, FusionProtocol.SourcePlayer);
        Assert.Equal(FusionProtocol.SourcePlayer, message[^1]);
    }

    [Fact]
    public void A_catch_up_spawn_never_uses_a_tracker_a_client_is_waiting_on()
    {
        // A client numbers its own spawn trackers from zero upward, so a
        // catch-up naming zero could complete a callback it was waiting on with
        // the wrong object.
        byte[] message = FusionProtocol.BuildSpawnResponse(
            1, 1, 300, "Pack.Spawnable.Crate", new Vec3(0, 0, 0), null, uint.MaxValue);

        var fusion = new OracleReader(message);

        fusion.ReadByte();
        fusion.ReadByte();
        fusion.ReadByte();
        fusion.ReadNullableByte();
        fusion.ReadInt32();
        fusion.ReadByte();          // owner
        fusion.ReadUInt16();        // entity id
        fusion.ReadString();        // barcode
        for (int i = 0; i < 3; i++) { fusion.ReadSingle(); }
        for (int i = 0; i < 7; i++) { fusion.ReadByte(); }

        Assert.Equal(uint.MaxValue, fusion.ReadUInt32());
    }

    [Fact]
    public void A_cosmetic_going_on_and_coming_off_reads_back()
    {
        foreach (bool equipped in new[] { true, false })
        {
            var data = new OracleWriter();
            data.Write("SLZ.BONELAB.Content.PointItem.Hat");
            data.Write(equipped);

            var message = new OracleWriter();
            message.Write((byte)206);
            message.Write((byte)3);     // ToOtherClients
            message.Write((byte)0);
            message.Write((byte?)3);
            message.Write(data.ToArray());

            var state = GateProtocol.TryReadEquipState(message.ToArray());

            Assert.NotNull(state);
            Assert.Equal("SLZ.BONELAB.Content.PointItem.Hat", state!.Value.Barcode);
            Assert.Equal(equipped, state.Value.Equipped);
        }
    }
}
