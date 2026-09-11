using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Telling clients who owns an entity. Moved out of the server so a redirected
/// data request can send the same message to one player, and pinned to the bytes
/// the server sent before the move.
/// </summary>
public class OwnershipResponseTests
{
    [Fact]
    public void It_is_the_bytes_the_server_has_always_sent()
    {
        // Worked out from AnnounceOwner as it stood: tag, ToClients, Reliable, the
        // owner as sender, then a length prefixed owner and entity id.
        byte[] before = { 16, 2, 0, 1, 3, 0, 0, 0, 3, 3, 1, 44 };

        Assert.Equal(before, FusionProtocol.BuildOwnershipResponse(owner: 3, entityId: 300));
    }

    [Fact]
    public void It_is_the_bytes_Fusion_would_have_written()
    {
        var data = new OracleWriter();
        data.Write((byte)7);                // EntityPlayerData.PlayerID
        data.Write((ushort)65000);          // NetworkEntityReference.ID

        var expected = new OracleWriter();
        expected.Write((byte)16);           // EntityOwnershipResponse
        expected.Write((byte)2);            // ToClients
        expected.Write((byte)0);            // Reliable
        expected.Write((byte?)7);           // sender
        expected.Write(data.ToArray());

        Assert.Equal(expected.ToArray(), FusionProtocol.BuildOwnershipResponse(7, 65000));
    }

    [Fact]
    public void The_owner_and_entity_read_back()
    {
        var read = FusionProtocol.TryReadOwnershipResponse(FusionProtocol.BuildOwnershipResponse(3, 300));

        Assert.Equal((byte)3, read!.Value.PlayerId);
        Assert.Equal((ushort)300, read.Value.EntityId);
    }
}
