using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Scenarios;

/// <summary>Scenario 4: a new mobile spawned next to a player whose holster already holds something.</summary>
public class HolsterScenario
{
    private const ushort Gun = 500;
    private const ushort Mobile = 501;

    [Fact]
    public void A_joiner_sees_the_same_holsters_as_everyone_after_a_new_phone_is_passed_around()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kekkis = world.Join(76561198000000002, "Kekkis79");
        kekkis.FinishLoading();

        world.Spawn(joel, Gun, "Pack.Spawnable.Gun", 0, 0, 0);
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, Gun, 0));

        world.Spawn(joel, Mobile, "spudgun1001.Payphone.Spawnable.Mobile", 0, 0, 0);
        world.Sync();

        kekkis.Send(FusionProtocol.BuildGrab(kekkis.SmallId, FusionProtocol.Handedness.RIGHT, 0, Mobile));
        kekkis.Send(FusionProtocol.BuildOwnershipRequest(kekkis.SmallId, Mobile));
        kekkis.Send(ClientMessages.SlotInsert(kekkis.SmallId, kekkis.SmallId, Mobile, 1));

        // A real hand lets go of what it holsters: GrabHelper.Internal_ObjectDetach
        // (LabFusion.Grabbables/GrabHelper.cs:150-160) sends PlayerRepRelease once
        // hand.m_CurrentAttachedGO is null, which is what the slot receiver leaves it as.
        kekkis.Send(FusionProtocol.BuildRelease(kekkis.SmallId, FusionProtocol.Handedness.RIGHT));

        var late = world.Join(76561198000000003, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        Assert.True(world.SlotsAgree(), "holster slots differ between players");
        Assert.Equal(Mobile, late.View.Slots[(kekkis.SmallId, 1)]);
        Assert.Equal(Gun, late.View.Slots[(joel.SmallId, 0)]);
    }

    [Fact]
    public void A_phone_removed_while_holstered_is_not_put_back_for_a_joiner()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Spawn(joel, Mobile, "spudgun1001.Payphone.Spawnable.Mobile", 0, 0, 0);
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, Mobile, 0));
        joel.Send(ClientMessages.Despawn(joel.SmallId, Mobile));

        var late = world.Join(76561198000000002, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        Assert.False(late.View.Slots.ContainsValue(Mobile));
    }
}
