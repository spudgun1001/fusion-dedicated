using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Each client message a fake player sends reads back through the server's own reader.</summary>
public class ClientMessagesTests
{
    [Fact]
    public void A_metadata_request_reads_back()
        => Assert.Equal(new FusionProtocol.MetadataRequest(3, "Loading", "False"),
            FusionProtocol.TryReadMetadataRequest(ClientMessages.FinishedLoading(3)));

    [Fact]
    public void A_despawn_request_reads_back()
        => Assert.Equal(((ushort)300, false), ServerProtocol.TryReadDespawnRequest(ClientMessages.Despawn(3, 300)));

    [Fact]
    public void A_slot_insert_reads_back()
    {
        byte[] message = ClientMessages.SlotInsert(3, slot: 3, weapon: 500, index: 1);

        var change = ModuleProtocol.ReadAttachment(
            ModuleProtocol.TryReadHandlerTag(message)!.Value, ModuleProtocol.TryReadHandlerPayload(message));

        Assert.Equal(new ModuleProtocol.AttachmentChange(ModuleProtocol.AttachmentKind.SlotInsert, 500, 3, 1), change);
    }

    [Fact]
    public void A_slot_drop_reads_back()
    {
        byte[] message = ClientMessages.SlotDrop(3, slot: 3, grabber: 4, index: 1, hand: 2);

        var change = ModuleProtocol.ReadAttachment(
            ModuleProtocol.TryReadHandlerTag(message)!.Value, ModuleProtocol.TryReadHandlerPayload(message));

        Assert.Equal(ModuleProtocol.AttachmentKind.SlotDrop, change.Kind);
        Assert.Equal((ushort)3, change.Slot);
        Assert.Equal((byte)1, change.SlotIndex);
    }

    [Fact]
    public void A_cull_status_reads_back()
        => Assert.Equal(((ushort)302, true), FusionProtocol.TryReadCullStatus(ClientMessages.CullStatus(3, 302, true)));

    [Fact]
    public void A_join_request_carries_the_name()
    {
        var config = new ServerConfig();

        var request = ServerProtocol.TryReadConnectionRequest(ClientMessages.Join(config, 76561198000000001, "Joel"));

        Assert.NotNull(request);
        Assert.Equal(76561198000000001UL, request!.PlatformId);
        Assert.Equal("Joel", request.Metadata["Username"]);
    }

    [Fact]
    public void Slot_messages_go_to_the_other_clients_like_a_real_client()
    {
        Assert.Equal(3, ClientMessages.SlotInsert(3, slot: 3, weapon: 500, index: 1)[1]);
        Assert.Equal(3, ClientMessages.SlotDrop(3, slot: 3, grabber: 4, index: 1, hand: 2)[1]);
    }
}
