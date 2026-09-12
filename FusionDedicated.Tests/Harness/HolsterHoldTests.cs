using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>A release message can be lost, so a slot insert must end the hold by itself.</summary>
public class HolsterHoldTests
{
    private const ushort Mobile = 501;

    [Fact]
    public void A_holstered_item_is_reseated_for_a_joiner_even_without_a_release()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kekkis = world.Join(76561198000000002, "Kekkis79");
        kekkis.FinishLoading();

        world.Spawn(joel, Mobile, "spudgun1001.Payphone.Spawnable.Mobile", 0, 0, 0);
        world.Sync();

        kekkis.Send(FusionProtocol.BuildGrab(kekkis.SmallId, FusionProtocol.Handedness.RIGHT, 0, Mobile));
        kekkis.Send(FusionProtocol.BuildOwnershipRequest(kekkis.SmallId, Mobile));
        kekkis.Send(ClientMessages.SlotInsert(kekkis.SmallId, kekkis.SmallId, Mobile, 1));

        var late = world.Join(76561198000000003, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(Mobile, late.View.Slots[(kekkis.SmallId, 1)]);
        Assert.Empty(world.Server.HoldersOf(Mobile));
    }
}
