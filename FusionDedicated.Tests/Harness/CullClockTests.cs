using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Culls must follow the world's own clock, not the wall clock, so a test can drive them without waiting.</summary>
public class CullClockTests
{
    [Fact]
    public void An_orphan_is_culled_once_the_world_clock_passes_the_orphan_timeout()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = true });
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 0, 0, 0);

        // Nobody else is online, so leaving orphans the crate instead of handing it to an heir.
        world.Leave(joel, "left");

        Assert.True(world.Server.Entities.Get(300)!.IsOrphaned);

        world.Advance(TimeSpan.FromSeconds(119));
        world.Tick();

        Assert.NotNull(world.Server.Entities.Get(300));

        world.Advance(TimeSpan.FromSeconds(2));
        world.Tick();

        Assert.Null(world.Server.Entities.Get(300));
    }
}
