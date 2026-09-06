using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class ModuleActionTests
{
    [Fact]
    public void Forward_means_carry_on_as_though_no_plugin_were_there()
    {
        Assert.Equal(ModuleActionKind.Forward, ModuleAction.Forward.Kind);
        Assert.Empty(ModuleAction.Forward.Payload);
    }

    [Fact]
    public void Drop_stops_the_message()
    {
        Assert.Equal(ModuleActionKind.Drop, ModuleAction.Drop.Kind);
    }

    [Fact]
    public void Rewrite_carries_the_new_payload()
    {
        var action = ModuleAction.Rewrite(new byte[] { 1, 2, 3 });

        Assert.Equal(ModuleActionKind.Rewrite, action.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, action.Payload);
    }

    [Fact]
    public void Reply_carries_what_goes_back_to_the_sender()
    {
        var action = ModuleAction.Reply(new byte[] { 9 });

        Assert.Equal(ModuleActionKind.Reply, action.Kind);
        Assert.Equal(new byte[] { 9 }, action.Payload);
    }

    [Fact]
    public void A_rewrite_with_nothing_in_it_is_a_drop_rather_than_an_empty_message()
    {
        // Sending an empty module message would be read by clients as a malformed
        // one, which is worse than sending none.
        Assert.Equal(ModuleActionKind.Drop, ModuleAction.Rewrite(Array.Empty<byte>()).Kind);
    }

    [Fact]
    public void A_request_carries_who_sent_it_and_what_it_holds()
    {
        var request = new ModuleRequest(1, "Joel", PermissionLevel.Default, 42, new byte[] { 7 });

        Assert.Equal(1ul, request.PlatformId);
        Assert.Equal(42, request.HandlerTag);
        Assert.Equal(new byte[] { 7 }, request.Payload);
    }
}
