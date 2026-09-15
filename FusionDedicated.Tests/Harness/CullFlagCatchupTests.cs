using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A joiner told that a prop's owner stopped simulating it takes the prop over when it comes
/// into view. Told that about a new owner who is still simulating it, both games fight over it.
/// </summary>
public class CullFlagCatchupTests
{
    [Fact]
    public void A_joiner_is_not_sent_a_cull_flag_left_by_a_previous_owner()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(joel, 302, "Pack.Spawnable.Crate", 40, 0, 40);
        joel.Send(ClientMessages.CullStatus(joel.SmallId, 302, true));

        world.Leave(joel, "Closing Connection");
        Assert.Equal((byte?)kanza.SmallId, world.Server.Entities.Get(302)!.OwnerSmallId);

        var late = world.Join(76561198000000003, "Late");
        late.FinishLoading();

        // Past both resends after loading and the first after joining.
        world.Advance(TimeSpan.FromSeconds(2));
        world.Advance(TimeSpan.FromSeconds(4));

        Assert.DoesNotContain(world.Transport.SentTo(late.Connection),
            sent => sent.Message[0] == FusionProtocol.TagEntityCullStatus);
        Assert.False(late.View.Entities[302].CulledForOwner);
    }
}
