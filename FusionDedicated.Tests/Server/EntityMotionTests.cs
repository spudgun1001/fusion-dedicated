using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class EntityMotionTests
{
    [Fact]
    public void A_pose_keeps_the_velocity_it_carried()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "a.b.Crate", 1, 0f, 0f, 0f);

        registry.NotePose(300, 1, 1f, 2f, 3f, null, 4f, 0f, -2f);

        var entity = registry.Get(300)!;
        Assert.Equal(4f, entity.VelocityX);
        Assert.Equal(0f, entity.VelocityY);
        Assert.Equal(-2f, entity.VelocityZ);
    }

    [Fact]
    public void A_pose_without_velocity_leaves_the_entity_still()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "a.b.Crate", 1, 0f, 0f, 0f);

        registry.NotePose(300, 1, 1f, 2f, 3f, null, 4f, 0f, 0f);
        registry.NotePose(300, 1, 1f, 2f, 3f);

        Assert.Equal(0f, registry.Get(300)!.VelocityX);
    }

    [Fact]
    public void A_discovered_entity_keeps_its_first_velocity()
    {
        var registry = new EntityRegistry();

        registry.NotePose(500, 7, 1f, 2f, 3f, null, 0f, 5f, 0f);

        Assert.Equal(5f, registry.Get(500)!.VelocityY);
    }
}
