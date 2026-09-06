using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Constraints travel inside a module message: native tag 200, then an eight byte
/// handler tag naming which handler it belongs to, then that handler's own payload.
/// The handler tag is a hash of the assembly and type names, which Fusion computes
/// with its own hash rather than the runtime's, so it can be worked out here.
/// </summary>
public class ModuleProtocolTests
{
    [Theory]
    [InlineData("LabFusion", 512426255)]
    [InlineData("LabFusion.Marrow.Messages.ConstraintCreateMessage", 1289102399)]
    public void The_hash_matches_Fusions_own(string text, int expected)
    {
        Assert.Equal(expected, ModuleProtocol.DeterministicHash(text));
    }

    [Fact]
    public void An_empty_string_hashes_without_throwing()
    {
        Assert.Equal(ModuleProtocol.DeterministicHash(""), ModuleProtocol.DeterministicHash(""));
    }

    [Fact]
    public void The_two_halves_are_packed_left_then_right()
    {
        Assert.Equal(0x0000000100000002L, ModuleProtocol.MakeLong(1, 2));
    }

    [Fact]
    public void A_negative_right_half_does_not_bleed_into_the_left()
    {
        Assert.Equal(0x00000001FFFFFFFFL, ModuleProtocol.MakeLong(1, -1));
    }

    [Fact]
    public void The_constraint_handler_tag_is_pinned()
    {
        // Changing this silently stops the server recognising a constraint at all,
        // so it is written down rather than only computed.
        Assert.Equal(2200854008125858879L, ModuleProtocol.ConstraintCreateTag);
    }

    [Fact]
    public void A_module_message_reports_the_handler_it_is_for()
    {
        byte[] message = ModuleProtocol.WriteModuleToClients(
            ModuleProtocol.ConstraintCreateTag, 4, new byte[] { 9, 9 });

        Assert.Equal(ModuleProtocol.ConstraintCreateTag, ModuleProtocol.TryReadHandlerTag(message));
    }

    [Fact]
    public void A_message_that_is_not_a_module_reports_nothing()
    {
        Assert.Null(ModuleProtocol.TryReadHandlerTag(new byte[] { 20, 1, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void A_module_message_too_short_to_hold_a_tag_reports_nothing()
    {
        Assert.Null(ModuleProtocol.TryReadHandlerTag(new byte[] { 200, 1, 0, 1, 3 }));
    }

    [Fact]
    public void The_handler_payload_comes_back_whole()
    {
        byte[] payload = { 1, 2, 3, 4, 5 };

        byte[] message = ModuleProtocol.WriteModuleToClients(
            ModuleProtocol.ConstraintCreateTag, 4, payload);

        Assert.Equal(payload, ModuleProtocol.TryReadHandlerPayload(message));
    }

    [Fact]
    public void The_last_two_ids_are_replaced_in_place()
    {
        // Point1Id and Point2Id are the final two fields, so the ids can be written
        // without understanding any of the constraint data before them.
        byte[] payload = { 7, 7, 7, 0, 0, 0, 0 };

        byte[] rewritten = ModuleProtocol.WithPointIds(payload, 0x0102, 0x0304);

        Assert.Equal(new byte[] { 7, 7, 7, 0x01, 0x02, 0x03, 0x04 }, rewritten);
    }

    [Fact]
    public void A_payload_too_short_to_hold_the_ids_is_refused()
    {
        Assert.Null(ModuleProtocol.WithPointIds(new byte[] { 1, 2, 3 }, 1, 2));
    }

    [Fact]
    public void Rewriting_does_not_disturb_the_original()
    {
        byte[] payload = { 7, 7, 7, 0, 0, 0, 0 };

        ModuleProtocol.WithPointIds(payload, 1, 2);

        Assert.Equal(new byte[] { 7, 7, 7, 0, 0, 0, 0 }, payload);
    }
}
