using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A client that has just been told an entity exists asks its owner for the rest,
/// which is how a gun in somebody's hand ends up in that hand for a newcomer. The
/// server reads the request so it can send it to the owner there is now.
/// </summary>
public class EntityDataRequestTests
{
    /// <summary>An EntityDataRequest as CatchupManager sends one: ToTarget, to the owner.</summary>
    private static byte[] FusionRequest(byte requester, byte target, ushort entityId)
    {
        var data = new OracleWriter();
        data.Write(requester);              // EntityPlayerData.PlayerID
        data.Write(entityId);               // NetworkEntityReference.ID

        var message = new OracleWriter();
        message.Write((byte)79);            // EntityDataRequest
        message.Write((byte)4);             // ToTarget
        message.Write((byte)0);             // Reliable
        message.Write((byte?)target);       // the route's target
        message.Write((byte?)requester);    // sender
        message.Write(data.ToArray());

        return message.ToArray();
    }

    [Fact]
    public void A_request_written_by_Fusion_reads_back()
    {
        var request = FusionProtocol.TryReadEntityDataRequest(FusionRequest(3, 5, 4242));

        Assert.NotNull(request);
        Assert.Equal((byte?)5, request!.Value.Target);
        Assert.Equal(3, request.Value.PlayerId);
        Assert.Equal(4242, request.Value.EntityId);
    }

    [Fact]
    public void A_request_to_player_zero_reads_back_its_target()
    {
        // Player 0 is the host in Fusion, and nobody at all on this server.
        var request = FusionProtocol.TryReadEntityDataRequest(FusionRequest(3, 0, 300));

        Assert.Equal((byte?)0, request!.Value.Target);
    }

    [Fact]
    public void A_request_on_any_other_route_has_no_target()
    {
        var data = new OracleWriter();
        data.Write((byte)3);
        data.Write((ushort)300);

        var message = new OracleWriter();
        message.Write((byte)79);
        message.Write((byte)3);             // ToOtherClients
        message.Write((byte)0);
        message.Write((byte?)3);
        message.Write(data.ToArray());

        var request = FusionProtocol.TryReadEntityDataRequest(message.ToArray());

        Assert.Null(request!.Value.Target);
        Assert.Equal(300, request.Value.EntityId);
    }

    [Fact]
    public void Our_forwarded_request_is_the_bytes_Fusion_would_have_written()
        => Assert.Equal(FusionRequest(3, 5, 4242), FusionProtocol.BuildEntityDataRequest(3, 5, 4242));

    [Fact]
    public void The_forwarded_request_goes_to_the_owner_as_the_player_who_asked()
    {
        byte[] message = FusionProtocol.BuildEntityDataRequest(requester: 3, target: 5, entityId: 300);
        var route = ServerProtocol.ReadRoute(message);

        var fusion = new OracleReader(message);
        fusion.ReadByte();                  // tag
        fusion.ReadByte();                  // relay
        fusion.ReadByte();                  // channel
        fusion.ReadNullableByte();          // target

        Assert.Equal(4, route.RelayType);
        Assert.Equal((byte?)5, route.Target);
        Assert.Equal((byte?)3, fusion.ReadNullableByte());
    }

    [Fact]
    public void Anything_else_is_refused_rather_than_throwing()
    {
        byte[] request = FusionRequest(3, 5, 300);

        Assert.Null(FusionProtocol.TryReadEntityDataRequest(FusionProtocol.BuildOwnershipResponse(3, 300)));
        Assert.Null(FusionProtocol.TryReadEntityDataRequest(request[..^1]));
        Assert.Null(FusionProtocol.TryReadEntityDataRequest(Array.Empty<byte>()));
    }
}
