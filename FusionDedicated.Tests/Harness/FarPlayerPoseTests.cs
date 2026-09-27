using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Player poses were the largest outbound after voice. A player far from somebody is sent only
/// every third of their poses, which Fusion carries over by moving the body on at its last velocity.
/// </summary>
public class FarPlayerPoseTests
{
    private const ushort Car = 400;

    private sealed record Scene(World World, FakePlayer Mover, FakePlayer Near, FakePlayer Far);

    /// <summary>A mover 200 m out, one player 10 m from them and one 100 m.</summary>
    private static Scene Build(ServerConfig? config = null, bool farHasPosition = true)
    {
        var world = new World(config ?? new ServerConfig { CullOrphanedEntities = false });
        var mover = world.Join(76561198000000001, "Dennis");
        var near = world.Join(76561198000000002, "Joel");
        var far = world.Join(76561198000000003, "Kanza");

        foreach (var player in new[] { mover, near, far })
        {
            player.FinishLoading();
        }

        StandAt(mover, new Vec3(200, 0, 0));
        StandAt(near, new Vec3(210, 0, 0));

        if (farHasPosition)
        {
            StandAt(far, new Vec3(300, 0, 0));
        }

        return new Scene(world, mover, near, far);
    }

    private static void StandAt(FakePlayer player, Vec3 position)
        => player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId, new FusionRigPose { PelvisPosition = position }));

    /// <summary>Poses of the mover's that reach a player while the mover sends six.</summary>
    private static int PosesSeen(Scene scene, FakePlayer player)
    {
        int PosesOf() => scene.World.Transport.SentTo(player.Connection)
            .Count(s => s.Message[0] == FusionProtocol.TagPlayerPoseUpdate && s.Message[4] == scene.Mover.SmallId);

        int before = PosesOf();

        for (int i = 0; i < 6; i++)
        {
            StandAt(scene.Mover, new Vec3(200, 0, 0));
        }

        return PosesOf() - before;
    }

    [Fact]
    public void A_near_player_gets_every_pose_and_a_far_one_every_third()
    {
        var scene = Build();
        using var world = scene.World;

        Assert.Equal(6, PosesSeen(scene, scene.Near));
        Assert.Equal(2, PosesSeen(scene, scene.Far));
    }

    [Fact]
    public void Zero_range_sends_every_pose_to_everyone()
    {
        var scene = Build(new ServerConfig { CullOrphanedEntities = false, FarPoseRange = 0 });
        using var world = scene.World;

        Assert.Equal(6, PosesSeen(scene, scene.Far));
    }

    [Fact]
    public void The_defaults_are_80_m_and_every_third_pose()
    {
        Assert.Equal(80f, new ServerConfig().FarPoseRange);
        Assert.Equal(3, new ServerConfig().FarPoseDivisor);
    }

    [Fact]
    public void A_far_player_whose_position_is_not_known_gets_every_pose()
    {
        var scene = Build(farHasPosition: false);
        using var world = scene.World;

        Assert.Equal(6, PosesSeen(scene, scene.Far));
    }

    [Fact]
    public void A_loading_far_player_gets_every_pose()
    {
        var scene = Build();
        using var world = scene.World;
        scene.Far.Send(ClientMessages.Metadata(scene.Far.SmallId, "Loading", "true"));

        Assert.Equal(6, PosesSeen(scene, scene.Far));
    }

    [Fact]
    public void A_seated_mover_is_sent_in_full_to_far_players()
    {
        var scene = Build();
        using var world = scene.World;
        world.Spawn(scene.Mover, Car, "spudgun1001.BabasPolice.Spawnable.SedanPolice", 200, 0, 0);
        scene.Mover.Send(FusionProtocol.BuildSeat(scene.Mover.SmallId, Car, 0, true));

        Assert.Equal(6, PosesSeen(scene, scene.Far));
    }

    [Fact]
    public void A_seated_far_player_gets_every_pose()
    {
        var scene = Build();
        using var world = scene.World;
        world.Spawn(scene.Far, Car, "spudgun1001.BabasPolice.Spawnable.SedanPolice", 300, 0, 0);
        scene.Far.Send(FusionProtocol.BuildSeat(scene.Far.SmallId, Car, 0, true));

        Assert.Equal(6, PosesSeen(scene, scene.Far));
    }
}
