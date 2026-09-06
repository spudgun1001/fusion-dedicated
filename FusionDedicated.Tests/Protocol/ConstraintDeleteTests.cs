using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

public class ConstraintDeleteTests
{
    [Fact]
    public void The_delete_tag_is_what_Fusion_hashes_it_to()
    {
        // Same shape as the create tag: assembly name and full type name, run
        // through Fusion's own hash. Wrong here and clearing a constraint is a
        // message the server never recognises.
        Assert.Equal(
            ModuleProtocol.TagFor("LabFusion", "LabFusion.Marrow.Messages.ConstraintDeleteMessage"),
            ModuleProtocol.ConstraintDeleteTag);
    }

    [Fact]
    public void Deleting_is_not_the_same_handler_as_creating()
        => Assert.NotEqual(ModuleProtocol.ConstraintCreateTag, ModuleProtocol.ConstraintDeleteTag);

    [Fact]
    public void The_payload_is_the_constraint_id_and_nothing_else()
    {
        // ConstraintDeleteData is one ushort, and says so: its Size is 2.
        var data = new FusionNetWriter(8);
        data.WriteUInt16(4242);

        byte[] message = ModuleProtocol.WriteModuleToClients(
            ModuleProtocol.ConstraintDeleteTag, 3, data.ToArray());

        byte[]? payload = ModuleProtocol.TryReadHandlerPayload(message);

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Length);
        Assert.Equal(4242, System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(payload));
    }

    [Fact]
    public void A_delete_is_recognised_from_the_message_it_arrives_in()
    {
        var data = new FusionNetWriter(8);
        data.WriteUInt16(7);

        byte[] message = ModuleProtocol.WriteModuleToClients(
            ModuleProtocol.ConstraintDeleteTag, 1, data.ToArray());

        Assert.Equal(ModuleProtocol.ConstraintDeleteTag, ModuleProtocol.TryReadHandlerTag(message));
    }
}
