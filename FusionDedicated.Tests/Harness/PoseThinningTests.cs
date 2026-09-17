using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Every prop's movement went to every player at 20 a second however far away they were.
/// Players far from a moving prop get fewer of its poses, and everything else is untouched.
/// </summary>
public class PoseThinningTests
{
    private const ushort Crate = 300;

    private static readonly Vec3 PropAt = new(0, 0, 0);

    private static ServerConfig Thinning(float distance = 60f, int farRate = 5) => new()
    {
        CullOrphanedEntities = false,
        PoseThinDistance = distance,
        FarPosesPerSecond = farRate,
    };

    private sealed record Scene(World World, FakePlayer Owner, FakePlayer Near, FakePlayer Far);

    /// <summary>An owner holding a crate at the origin, one player 5 m away and one 100 m away.</summary>
    private static Scene Build(ServerConfig config, bool farHasPosition = true)
    {
        var world = new World(config);
        var owner = world.Join(76561198000000001, "Dennis");
        var near = world.Join(76561198000000002, "Joel");
        var far = world.Join(76561198000000003, "Kanza");

        foreach (var player in new[] { owner, near, far })
        {
            player.FinishLoading();
        }

        world.Spawn(owner, Crate, "Test.Crate", 0, 0, 0);
        StandAt(owner, new Vec3(1, 0, 0));
        StandAt(near, new Vec3(5, 0, 0));

        if (farHasPosition)
        {
            StandAt(far, new Vec3(100, 0, 0));
        }

        return new Scene(world, owner, near, far);
    }

    private static void StandAt(FakePlayer player, Vec3 position)
        => player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId, new FusionRigPose { PelvisPosition = position }));

    private static byte[] MovingPose(FakePlayer owner)
        => FusionProtocol.BuildEntityPoseUpdate(owner.SmallId, Crate, PropAt, Quat.Identity, new Vec3(0, 1, 0), default);

    /// <summary>The pose a prop sends once as it goes to sleep, on the reliable channel.</summary>
    private static byte[] RestingPose(FakePlayer owner)
    {
        byte[] message = MovingPose(owner);
        message[2] = 0;

        return message;
    }

    private static int Count(World world, FakePlayer player, byte tag, int before)
        => world.Transport.SentTo(player.Connection).Skip(before).Count(sent => sent.Message[0] == tag);

    private static int Sent(World world, FakePlayer player) => world.Transport.SentTo(player.Connection).Count;

    /// <summary>A second of poses at Fusion's 20 a second.</summary>
    private static void OneSecondOfPoses(Scene scene, Func<FakePlayer, byte[]> pose)
    {
        for (var i = 0; i < 20; i++)
        {
            scene.Owner.Send(pose(scene.Owner));
            scene.World.Advance(TimeSpan.FromMilliseconds(50));
        }
    }

    [Fact]
    public void A_far_player_gets_a_moving_prop_at_the_far_rate_and_a_near_one_gets_every_pose()
    {
        var scene = Build(Thinning());
        using var world = scene.World;
        int near = Sent(world, scene.Near);
        int far = Sent(world, scene.Far);

        OneSecondOfPoses(scene, MovingPose);

        Assert.Equal(20, Count(world, scene.Near, FusionProtocol.TagEntityPoseUpdate, near));
        Assert.Equal(5, Count(world, scene.Far, FusionProtocol.TagEntityPoseUpdate, far));
    }

    [Fact]
    public void A_prop_coming_to_rest_always_reaches_a_far_player()
    {
        var scene = Build(Thinning());
        using var world = scene.World;
        scene.Owner.Send(MovingPose(scene.Owner));
        int far = Sent(world, scene.Far);

        scene.Owner.Send(MovingPose(scene.Owner));
        scene.Owner.Send(RestingPose(scene.Owner));

        Assert.Equal(1, Count(world, scene.Far, FusionProtocol.TagEntityPoseUpdate, far));
    }

    [Fact]
    public void A_player_whose_position_is_not_known_gets_every_pose()
    {
        var scene = Build(Thinning(), farHasPosition: false);
        using var world = scene.World;
        int far = Sent(world, scene.Far);

        OneSecondOfPoses(scene, MovingPose);

        Assert.Equal(20, Count(world, scene.Far, FusionProtocol.TagEntityPoseUpdate, far));
    }

    [Theory]
    [InlineData(0f, 5)]
    [InlineData(60f, 0)]
    public void Zero_in_either_setting_turns_thinning_off(float distance, int farRate)
    {
        var scene = Build(Thinning(distance, farRate));
        using var world = scene.World;
        int far = Sent(world, scene.Far);

        OneSecondOfPoses(scene, MovingPose);

        Assert.Equal(20, Count(world, scene.Far, FusionProtocol.TagEntityPoseUpdate, far));
    }

    [Fact]
    public void Player_movement_is_never_thinned()
    {
        var scene = Build(Thinning());
        using var world = scene.World;
        int far = Sent(world, scene.Far);

        OneSecondOfPoses(scene, owner =>
            FusionProtocol.BuildPlayerPoseUpdate(owner.SmallId, new FusionRigPose { PelvisPosition = new Vec3(1, 0, 0) }));

        Assert.Equal(20, Count(world, scene.Far, FusionProtocol.TagPlayerPoseUpdate, far));
    }
}
