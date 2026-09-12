using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Scenarios;

/// <summary>Scenario 5: owners leaving while somebody else holds or rides what they owned.</summary>
public class DepartureScenario
{
    [Fact]
    public void Whoever_holds_an_item_takes_it_when_its_owner_leaves()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();
        var dennis = world.Join(76561198000000003, "Dennis");
        dennis.FinishLoading();

        world.Spawn(joel, 600, "Pack.Spawnable.Gun", 0, 0, 0);
        world.Sync();

        // Kanza would get it anyway as the first player left, so Dennis holds it.
        dennis.Send(FusionProtocol.BuildGrab(dennis.SmallId, FusionProtocol.Handedness.RIGHT, 0, 600));

        world.Leave(joel, "Closing Connection");

        Assert.Equal(dennis.SmallId, world.Server.Entities.Get(600)!.OwnerSmallId);
        Assert.True(world.AgreeOnOwner(600), string.Join(", ", world.OwnersOf(600)));
        Assert.All(world.Players, p => Assert.Equal(dennis.SmallId, p.View.Entities[600].Owner));
    }

    [Fact]
    public void A_rider_takes_a_car_when_its_driver_leaves()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();
        var dennis = world.Join(76561198000000003, "Dennis");
        dennis.FinishLoading();

        world.Spawn(joel, 601, "BaBaCorp.AssortedAutomobiles.Spawnable.VindleVobbleVan", 0, 0, 0);
        world.Sync();
        joel.Send(FusionProtocol.BuildSeat(joel.SmallId, 601, 0, true));

        // Kanza would get it anyway as the first player left, so Dennis is the one riding.
        dennis.Send(FusionProtocol.BuildSeat(dennis.SmallId, 601, 1, true));

        world.Leave(joel, "Timeout");

        Assert.Equal(dennis.SmallId, world.Server.Entities.Get(601)!.OwnerSmallId);
        Assert.True(world.AgreeOnOwner(601), string.Join(", ", world.OwnersOf(601)));
        Assert.All(world.Players, p => Assert.Equal(dennis.SmallId, p.View.Entities[601].Owner));
    }
}
