using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A game with a broken body sent poses that turned its rig to NaN in everybody else's game, and the
/// physics took their own bodies down with it, so they froze and timed out over and over (1 Oct). The
/// server passes nothing on that no working game could send.
/// </summary>
public class BrokenPoseTests
{
    private static (World World, FakePlayer Sender, FakePlayer Watcher) Build()
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var sender = world.Join(76561198000000001, "Iceyy");
        var watcher = world.Join(76561198000000002, "Enzo");
        sender.FinishLoading();
        watcher.FinishLoading();
        return (world, sender, watcher);
    }

    private static int PosesSeen(World world, FakePlayer watcher, FakePlayer sender)
        => world.Transport.SentTo(watcher.Connection)
            .Count(s => s.Message[0] == FusionProtocol.TagPlayerPoseUpdate && s.Message[4] == sender.SmallId);

    private static void Send(FakePlayer player, FusionRigPose pose)
        => player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId, pose));

    public static IEnumerable<object[]> Broken()
    {
        yield return new object[] { new FusionRigPose { PelvisPosition = new Vec3(float.NaN, 0, 0) } };
        yield return new object[] { new FusionRigPose { PelvisVelocity = new Vec3(float.PositiveInfinity, 0, 0) } };
        yield return new object[] { new FusionRigPose { Health = float.NaN } };
        yield return new object[] { new FusionRigPose { CrouchTarget = float.NegativeInfinity } };
        yield return new object[] { new FusionRigPose { TrackedRotations = { [1] = new Quat(0, 0, 0, 0) } } };
        yield return new object[] { new FusionRigPose { PelvisRotation = new Quat(0, 0, 0, 0) } };
    }

    [Theory]
    [MemberData(nameof(Broken))]
    public void A_broken_pose_reaches_nobody(FusionRigPose pose)
    {
        var (world, sender, watcher) = Build();
        using var _ = world;
        int before = PosesSeen(world, watcher, sender);

        Send(sender, pose);

        Assert.Equal(before, PosesSeen(world, watcher, sender));
    }

    [Fact]
    public void A_good_pose_still_reaches_everybody()
    {
        var (world, sender, watcher) = Build();
        using var _ = world;
        int before = PosesSeen(world, watcher, sender);

        Send(sender, new FusionRigPose { PelvisPosition = new Vec3(160, 1, -130), Health = 80 });

        Assert.Equal(before + 1, PosesSeen(world, watcher, sender));
    }

    [Fact]
    public void A_broken_pose_does_not_move_the_player_and_says_who_sent_it()
    {
        var (world, sender, watcher) = Build();
        using var _ = world;
        Send(sender, new FusionRigPose { PelvisPosition = new Vec3(160, 1, -130) });

        Send(sender, new FusionRigPose { PelvisPosition = new Vec3(float.NaN, 0, 0) });
        Send(sender, new FusionRigPose { PelvisPosition = new Vec3(float.NaN, 0, 0) });

        Assert.Equal(160f, world.Server.Players.Get(sender.SmallId)!.LastPosition.X, 1);
        Assert.Single(world.Server.RecentLog(200), l => l.Message.Contains("broken pose from Iceyy"));
    }
}
