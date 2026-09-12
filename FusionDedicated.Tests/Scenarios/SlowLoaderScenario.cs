using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Scenarios;

/// <summary>Scenario 1: a player who takes 80 seconds to load after joining a busy world.</summary>
public class SlowLoaderScenario
{
    [Fact]
    public void A_slow_loader_agrees_with_everyone_once_loaded()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(joel, 300, "Pack.Spawnable.Gun", 1, 1, 1);
        world.Spawn(joel, 301, "Pack.Spawnable.Gun", 1, 1, 1);
        world.Spawn(kanza, 302, "Pack.Spawnable.Crate", 40, 0, 40);
        world.Spawn(joel, 303, "Pack.Spawnable.Crate", 2, 0, 2);

        joel.Send(FusionProtocol.BuildGrab(joel.SmallId, FusionProtocol.Handedness.RIGHT, 0, 300));
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, 301, 0));
        kanza.Send(ClientMessages.CullStatus(kanza.SmallId, 302, true));

        var slow = world.Join(76561198000000003, "Slow");
        world.Advance(TimeSpan.FromSeconds(3));
        world.Advance(TimeSpan.FromSeconds(6));
        world.Advance(TimeSpan.FromSeconds(71));

        // The crate changes hands while Slow is still loading.
        kanza.Send(FusionProtocol.BuildOwnershipRequest(kanza.SmallId, 303));

        slow.FinishLoading();

        world.Advance(TimeSpan.FromSeconds(2));
        world.Advance(TimeSpan.FromSeconds(4));

        Assert.True(world.AgreeOnOwner(300), string.Join(", ", world.OwnersOf(300)));
        Assert.True(world.AgreeOnOwner(301), string.Join(", ", world.OwnersOf(301)));
        Assert.True(world.AgreeOnOwner(302), string.Join(", ", world.OwnersOf(302)));
        Assert.True(world.SlotsAgree(), "holster slots differ between players");
        Assert.Equal(kanza.View.Entities[302].CulledForOwner, slow.View.Entities[302].CulledForOwner);
        Assert.Equal(kanza.SmallId, world.Server.Entities.Get(303)!.OwnerSmallId);
        Assert.True(world.AgreeOnOwner(303), string.Join(", ", world.OwnersOf(303)));
    }
}
