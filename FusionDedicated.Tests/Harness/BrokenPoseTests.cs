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

    // The same night a gun drawn from a holster froze the players near it, sent as the gun's pose, not the player's.
    private const ushort Gun = 489;

    private static int GunPosesSeen(World world, FakePlayer watcher)
        => world.Transport.SentTo(watcher.Connection)
            .Count(s => FusionProtocol.TryReadEntityPose(s.Message) is { EntityId: Gun });

    private static void SendGun(FakePlayer player, Vec3 position, Vec3 velocity)
        => player.Send(FusionProtocol.BuildEntityPoseUpdate(player.SmallId, Gun, position, Quat.Identity, velocity, default));

    public static IEnumerable<object[]> BrokenGun()
    {
        yield return new object[] { new Vec3(float.NaN, 1, 2), Vec3.Zero };
        yield return new object[] { new Vec3(1, 1, 2), new Vec3(0, float.PositiveInfinity, 0) };
    }

    [Theory]
    [MemberData(nameof(BrokenGun))]
    public void A_broken_entity_pose_reaches_nobody(Vec3 position, Vec3 velocity)
    {
        var (world, sender, watcher) = Build();
        using var _ = world;
        world.Spawn(sender, Gun, "Rexmeck.WeaponPackLT.Spawnable.Glock17", 0, 0, 0);
        SendGun(sender, new Vec3(1, 1, 2), Vec3.Zero);
        int before = GunPosesSeen(world, watcher);

        SendGun(sender, position, velocity);

        Assert.Equal(before, GunPosesSeen(world, watcher));
        Assert.Single(world.Server.RecentLog(200), l => l.Message.Contains("broken pose from Iceyy"));
    }

    [Fact]
    public void A_good_entity_pose_still_reaches_everybody()
    {
        var (world, sender, watcher) = Build();
        using var _ = world;
        world.Spawn(sender, Gun, "Rexmeck.WeaponPackLT.Spawnable.Glock17", 0, 0, 0);
        int before = GunPosesSeen(world, watcher);

        SendGun(sender, new Vec3(1, 1, 2), new Vec3(0, 1, 0));

        Assert.Equal(before + 1, GunPosesSeen(world, watcher));
    }

    // On 1 Oct two players were dropped by their own game a minute after somebody joined, and nothing said why.
    [Fact]
    public void A_player_who_drops_is_shown_what_reached_them_last()
    {
        var (world, sender, watcher) = Build();
        using var _ = world;
        Send(sender, new FusionRigPose { PelvisPosition = new Vec3(160, 1, -130) });

        world.Leave(watcher, "Closing Connection");

        var line = Assert.Single(world.Server.RecentLog(200), l => l.Message.Contains("What reached Enzo"));
        Assert.Contains($"tag {FusionProtocol.TagPlayerPoseUpdate}", line.Message);
        Assert.Contains($"from player {sender.SmallId}", line.Message);
    }

    [Fact]
    public void A_staff_kick_is_not_explained()
    {
        var (world, sender, watcher) = Build();
        using var _ = world;
        Send(sender, new FusionRigPose { PelvisPosition = new Vec3(160, 1, -130) });

        world.Leave(watcher, "Kicked by Kanzaaa");

        Assert.DoesNotContain(world.Server.RecentLog(200), l => l.Message.Contains("What reached"));
    }
}
