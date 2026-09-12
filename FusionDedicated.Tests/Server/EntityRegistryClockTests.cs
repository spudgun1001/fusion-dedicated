using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class EntityRegistryClockTests
{
    private static readonly DateTime Start = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_registered_entity_is_stamped_with_the_registry_clock()
    {
        var registry = new EntityRegistry { Clock = () => Start };

        var entity = registry.Register(300, "Pack.Spawnable.Crate", 1, 0, 0, 0);

        Assert.Equal(Start, entity.SpawnedAt);
        Assert.Equal(Start, entity.LastUpdate);
    }

    [Fact]
    public void A_pose_stamps_last_update_with_the_registry_clock()
    {
        var now = Start;
        var registry = new EntityRegistry { Clock = () => now };
        registry.Register(300, "Pack.Spawnable.Crate", 1, 0, 0, 0);

        now = Start.AddMinutes(5);
        registry.NotePose(300, 1, 1f, 2f, 3f);

        Assert.Equal(Start.AddMinutes(5), registry.Get(300)!.LastUpdate);
    }

    [Fact]
    public void An_orphan_is_kept_until_the_registry_clock_passes_the_cutoff()
    {
        var now = Start;
        var registry = new EntityRegistry { Clock = () => now };
        registry.Register(300, "Test.Prop", 1, 0, 0, 0);
        registry.SetOwner(300, null);

        now = Start.AddMinutes(1);
        Assert.Empty(registry.CullOrphans(TimeSpan.FromMinutes(2)));

        now = Start.AddMinutes(3);
        Assert.Equal(new ushort[] { 300 }, registry.CullOrphans(TimeSpan.FromMinutes(2)));
    }
}
