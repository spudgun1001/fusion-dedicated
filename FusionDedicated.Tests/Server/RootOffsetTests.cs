using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A spawn places a crate's root, but a pose only carries its first body. The first pose after
/// the spawn says how that body sits against the root, so a joiner can be sent the root.
/// </summary>
public class RootOffsetTests
{
    private static readonly DateTime Spawned = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly float Half = MathF.Sqrt(0.5f);
    private static readonly Quat QuarterAboutY = new(0, Half, 0, Half);
    private static readonly Quat HalfAboutY = new(0, 1, 0, 0);
    private static readonly Vec3 Root = new(10, 0, 10);
    private static readonly SpawnRoot UprightRoot = new(Root, Rotations.Encode(Quat.Identity));

    /// <summary>The first body, two metres along from the root and a quarter turn round.</summary>
    private static readonly Vec3 Body = new(10, 0, 12);

    private static bool SameRotation(Quat a, Quat b)
        => MathF.Abs((a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W)) > 0.9999f;

    private static void AssertNear(Vec3 expected, Vec3 actual)
    {
        float distance = new Vec3(expected.X - actual.X, expected.Y - actual.Y, expected.Z - actual.Z).Magnitude;
        Assert.True(distance <= 1e-3f, $"expected {expected}, got {actual}");
    }

    private static RootOffset? CaptureAt(Vec3 body, double secondsAfterSpawn = 1, byte[]? rotation = null)
        => RootOffset.Capture(UprightRoot, Spawned, Spawned.AddSeconds(secondsAfterSpawn), body,
            rotation ?? Rotations.Encode(QuarterAboutY));

    [Fact]
    public void The_pose_it_was_learned_from_gives_back_the_root()
    {
        var root = CaptureAt(Body)!.Value.Apply(Body, Rotations.Encode(QuarterAboutY))!.Value;

        AssertNear(Root, root.Position);
        Assert.True(SameRotation(Quat.Identity, Rotations.TryDecode(root.Rotation)!.Value));
    }

    [Fact]
    public void The_root_follows_the_body_when_it_moves_and_turns()
    {
        var root = CaptureAt(Body)!.Value.Apply(new Vec3(20, 0, 20), Rotations.Encode(HalfAboutY))!.Value;

        AssertNear(new Vec3(18, 0, 20), root.Position);
        Assert.True(SameRotation(QuarterAboutY, Rotations.TryDecode(root.Rotation)!.Value));
    }

    [Fact]
    public void The_roots_own_turn_is_kept()
    {
        var turnedRoot = new SpawnRoot(Vec3.Zero, Rotations.Encode(QuarterAboutY));
        var offset = RootOffset.Capture(turnedRoot, Spawned, Spawned, Vec3.Zero, Rotations.Encode(Quat.Identity))!.Value;

        var root = offset.Apply(new Vec3(1, 0, 0), Rotations.Encode(Quat.Identity))!.Value;

        AssertNear(new Vec3(1, 0, 0), root.Position);
        Assert.True(SameRotation(QuarterAboutY, Rotations.TryDecode(root.Rotation)!.Value));
    }

    [Fact]
    public void A_body_too_far_from_the_root_gives_no_offset()
        => Assert.Null(CaptureAt(new Vec3(10, 0, 16)));

    [Fact]
    public void A_body_at_the_distance_limit_still_gives_an_offset()
        => Assert.NotNull(CaptureAt(new Vec3(10, 0, 15)));

    [Fact]
    public void A_pose_too_long_after_the_spawn_gives_no_offset()
        => Assert.Null(CaptureAt(Body, secondsAfterSpawn: 11));

    [Fact]
    public void A_pose_at_the_time_limit_still_gives_an_offset()
        => Assert.NotNull(CaptureAt(Body, secondsAfterSpawn: 10));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    public void A_body_rotation_of_the_wrong_length_gives_no_offset(int length)
        => Assert.Null(CaptureAt(Body, rotation: new byte[length]));

    [Fact]
    public void A_body_position_that_is_not_a_number_gives_no_offset()
        => Assert.Null(CaptureAt(new Vec3(float.NaN, 0, 12)));

    [Fact]
    public void A_root_rotation_of_the_wrong_length_counts_as_upright()
    {
        // BuildSpawnResponse sends upright for any other length, so that is how clients spawned it.
        var root = new SpawnRoot(Root, Array.Empty<byte>());
        var offset = RootOffset.Capture(root, Spawned, Spawned, Body, Rotations.Encode(QuarterAboutY))!.Value;

        var placed = offset.Apply(Body, Rotations.Encode(QuarterAboutY))!.Value;

        Assert.True(SameRotation(Quat.Identity, Rotations.TryDecode(placed.Rotation)!.Value));
    }

    [Fact]
    public void Applying_to_an_unreadable_rotation_gives_nothing()
        => Assert.Null(CaptureAt(Body)!.Value.Apply(Body, new byte[3]));

    [Fact]
    public void Applying_to_a_position_that_is_not_a_number_gives_nothing()
        => Assert.Null(CaptureAt(Body)!.Value.Apply(new Vec3(float.NaN, 0, 0), Rotations.Encode(QuarterAboutY)));
}
