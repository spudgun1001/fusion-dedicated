using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Spray mist and magazines piled up to ~670 entities in a day, since a
/// connected owner's props are never idle-culled.
/// </summary>
public class ShortLivedCullTests
{
    [Fact]
    public void Defaults_put_mist_on_a_one_minute_ammo_clock()
    {
        var config = new ServerConfig();

        Assert.Equal(60, config.AmmoTimeoutSeconds);
        Assert.Contains("Mist", config.ShortLivedBarcodes);
    }

    [Fact]
    public void Spray_mist_goes_on_the_ammo_clock_while_its_owner_is_still_here()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = true });
        var kanza = world.Join(76561198000000001, "Kanza");
        kanza.FinishLoading();

        world.Spawn(kanza, 300, "BaBaCorp.AssortedAutomobiles.Spawnable.SpraypaintMist", 0, 0, 0);
        world.Spawn(kanza, 301, "Pack.Spawnable.Crate", 0, 0, 0);

        // The spray can's crate spawner makes it, so it is a scene spawn.
        world.Server.Entities.Get(300)!.Source = BonelabServerBrowser.Fusion.FusionProtocol.SourceScene;

        world.Advance(TimeSpan.FromSeconds(61));
        world.Tick();

        Assert.Null(world.Server.Entities.Get(300));
        Assert.NotNull(world.Server.Entities.Get(301));
    }

    [Fact]
    public void A_dropped_magazine_goes_after_a_minute()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = true });
        var kanza = world.Join(76561198000000001, "Kanza");
        kanza.FinishLoading();

        world.Spawn(kanza, 300, "Rexmeck.WeaponPackLT.Spawnable.Magglock17gen5", 0, 0, 0);
        world.Server.Entities.Get(300)!.Source = BonelabServerBrowser.Fusion.FusionProtocol.SourceNone;

        world.Advance(TimeSpan.FromSeconds(61));
        world.Tick();

        Assert.Null(world.Server.Entities.Get(300));
    }
}
