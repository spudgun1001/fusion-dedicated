using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A game that says it is loading drops moving prop poses and re-seats (SkipHandleWhileLoading,
/// and the props are not built until the level is), so the server does not send them.
/// </summary>
public class LoadingTrafficTests
{
    private const ushort Crate = 300;

    private static byte[] StartedLoading(byte player) => ClientMessages.Metadata(player, "Loading", "True");

    private static byte[] MovingPose(FakePlayer owner)
        => FusionProtocol.BuildEntityPoseUpdate(owner.SmallId, Crate, new Vec3(0, 0, 0), Quat.Identity, new Vec3(0, 1, 0), default);

    private static int Count(World world, FakePlayer player, byte tag)
        => world.Transport.SentTo(player.Connection).Count(sent => sent.Message[0] == tag);

    private static (World World, FakePlayer Owner, FakePlayer Joiner) Build()
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var owner = world.Join(76561198000000001, "Dennis");
        owner.FinishLoading();
        world.Spawn(owner, Crate, "Test.Crate", 0, 0, 0);

        return (world, owner, world.Join(76561198000000002, "Joel"));
    }

    [Fact]
    public void A_loading_player_gets_no_moving_prop_poses_until_it_has_loaded()
    {
        var (world, owner, joiner) = Build();
        using var _ = world;
        joiner.Send(StartedLoading(joiner.SmallId));

        owner.Send(MovingPose(owner));
        Assert.Equal(0, Count(world, joiner, FusionProtocol.TagEntityPoseUpdate));

        joiner.FinishLoading();
        owner.Send(MovingPose(owner));
        Assert.Equal(1, Count(world, joiner, FusionProtocol.TagEntityPoseUpdate));
    }

    [Fact]
    public void A_prop_coming_to_rest_still_reaches_a_loading_player()
    {
        var (world, owner, joiner) = Build();
        using var _ = world;
        joiner.Send(StartedLoading(joiner.SmallId));

        byte[] resting = MovingPose(owner);
        resting[2] = 0;
        owner.Send(resting);

        Assert.Equal(1, Count(world, joiner, FusionProtocol.TagEntityPoseUpdate));
    }

    [Fact]
    public void A_player_who_never_said_it_is_loading_still_gets_moving_poses()
    {
        var (world, owner, joiner) = Build();
        using var _ = world;

        owner.Send(MovingPose(owner));

        Assert.Equal(1, Count(world, joiner, FusionProtocol.TagEntityPoseUpdate));
    }

    [Fact]
    public void Player_movement_still_reaches_a_loading_player()
    {
        var (world, owner, joiner) = Build();
        using var _ = world;
        joiner.Send(StartedLoading(joiner.SmallId));

        owner.Send(FusionProtocol.BuildPlayerPoseUpdate(owner.SmallId, new FusionRigPose()));

        Assert.Equal(1, Count(world, joiner, FusionProtocol.TagPlayerPoseUpdate));
    }

    [Fact]
    public void The_join_reseat_waits_for_a_loading_player_to_finish()
    {
        var (world, owner, joiner) = Build();
        using var _ = world;
        owner.Send(ClientMessages.CullStatus(owner.SmallId, Crate, true));
        joiner.Send(StartedLoading(joiner.SmallId));
        int relayed = Count(world, joiner, FusionProtocol.TagEntityCullStatus);

        // Past both re-seats after joining.
        world.Advance(TimeSpan.FromSeconds(3));
        world.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(relayed, Count(world, joiner, FusionProtocol.TagEntityCullStatus));

        joiner.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(relayed + 1, Count(world, joiner, FusionProtocol.TagEntityCullStatus));
    }

    [Fact]
    public void The_join_reseat_still_reaches_a_player_who_never_said_it_is_loading()
    {
        var (world, owner, joiner) = Build();
        using var _ = world;
        owner.Send(ClientMessages.CullStatus(owner.SmallId, Crate, true));
        int relayed = Count(world, joiner, FusionProtocol.TagEntityCullStatus);

        world.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(relayed + 1, Count(world, joiner, FusionProtocol.TagEntityCullStatus));
    }
}
