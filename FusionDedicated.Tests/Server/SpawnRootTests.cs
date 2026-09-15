using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>The registry learns a root offset from the first pose after a spawn, once, and then forgets the root.</summary>
public class SpawnRootTests
{
    private static readonly float Half = MathF.Sqrt(0.5f);
    private static readonly byte[] QuarterAboutY = Rotations.Encode(new Quat(0, Half, 0, Half));

    private DateTime _now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private EntityRegistry CarAtItsRoot()
    {
        var registry = new EntityRegistry { Clock = () => _now };
        var car = registry.Register(300, "Pack.Spawnable.Car", 1, 10, 0, 10, Rotations.Encode(Quat.Identity));
        car.SpawnRoot = new SpawnRoot(new Vec3(10, 0, 10), Rotations.Encode(Quat.Identity));
        return registry;
    }

    [Fact]
    public void The_first_pose_learns_the_offset_and_forgets_the_root()
    {
        var registry = CarAtItsRoot();

        registry.NotePose(300, 1, 10, 0, 12, QuarterAboutY);

        Assert.NotNull(registry.Get(300)!.RootOffset);
        Assert.Null(registry.Get(300)!.SpawnRoot);
    }

    [Fact]
    public void A_later_pose_does_not_change_the_offset()
    {
        var registry = CarAtItsRoot();
        registry.NotePose(300, 1, 10, 0, 12, QuarterAboutY);
        var learned = registry.Get(300)!.RootOffset;

        registry.NotePose(300, 1, 20, 0, 20, Rotations.Encode(new Quat(0, 1, 0, 0)));

        Assert.Equal(learned, registry.Get(300)!.RootOffset);
    }

    [Fact]
    public void A_first_pose_too_far_away_forgets_the_root_and_learns_nothing()
    {
        var registry = CarAtItsRoot();

        registry.NotePose(300, 1, 10, 0, 30, QuarterAboutY);

        Assert.Null(registry.Get(300)!.RootOffset);
        Assert.Null(registry.Get(300)!.SpawnRoot);
    }

    [Fact]
    public void A_first_pose_too_late_forgets_the_root_and_learns_nothing()
    {
        var registry = CarAtItsRoot();
        _now += TimeSpan.FromSeconds(11);

        registry.NotePose(300, 1, 10, 0, 12, QuarterAboutY);

        Assert.Null(registry.Get(300)!.RootOffset);
        Assert.Null(registry.Get(300)!.SpawnRoot);
    }

    [Fact]
    public void A_first_pose_with_no_rotation_forgets_the_root_and_learns_nothing()
    {
        var registry = CarAtItsRoot();

        registry.NotePose(300, 1, 10, 0, 12);

        Assert.Null(registry.Get(300)!.RootOffset);
        Assert.Null(registry.Get(300)!.SpawnRoot);
    }

    [Fact]
    public void An_entity_with_no_root_learns_nothing()
    {
        var registry = new EntityRegistry { Clock = () => _now };
        registry.Register(301, "Pack.Spawnable.Crate", 1, 0, 0, 0);

        registry.NotePose(301, 1, 0, 0, 2, QuarterAboutY);

        Assert.Null(registry.Get(301)!.RootOffset);
    }
}
