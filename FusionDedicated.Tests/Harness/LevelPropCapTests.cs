using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Tests.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A pose for an id nobody spawned registers a level prop that no cull removes, so a
/// client posing made-up ids could fill the world. Past the per player limit the
/// server stops tracking them but still passes the pose on.
/// </summary>
public class LevelPropCapTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    private static World LimitOfTwo()
        => new(new ServerConfig { CullOrphanedEntities = false, MaxEntitiesPerPlayer = 2 });

    private static void Pose(FakePlayer player, ushort id)
        => player.Send(FusionProtocol.BuildEntityPoseUpdate(player.SmallId, id, new Vec3(1, 2, 3), default, default, default));

    [Fact]
    public void Poses_for_unknown_ids_stop_registering_at_the_per_player_cap_and_keep_registering_for_another_player()
    {
        using var world = LimitOfTwo();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        Pose(joel, 500);
        Pose(joel, 501);
        Pose(joel, 502);

        Assert.NotNull(world.Server.Entities.Get(500));
        Assert.NotNull(world.Server.Entities.Get(501));
        Assert.Null(world.Server.Entities.Get(502));

        Pose(kanza, 503);

        Assert.Equal((byte?)kanza.SmallId, world.Server.Entities.Get(503)!.OwnerSmallId);
    }

    /// <summary>Networks a level object the way a first grab or sit does.</summary>
    private static void Unqueue(FakePlayer player, ushort queuedId)
    {
        var data = new OracleWriter();
        data.Write(player.SmallId);
        data.Write(queuedId);

        var message = new OracleWriter();
        message.Write(FusionProtocol.TagEntityUnqueueRequest);
        message.Write((byte)1);         // ToServer
        message.Write((byte)0);         // Reliable
        message.Write((byte?)player.SmallId);
        message.Write(data.ToArray());

        player.Send(message.ToArray());
    }

    [Fact]
    public void A_level_seat_somebody_sat_in_does_not_count_toward_their_limit()
    {
        using var world = LimitOfTwo();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        Unqueue(joel, 1);
        ushort seat = world.Server.Entities.Entities.Single(e => e.Discovered).Id;
        joel.Send(FusionProtocol.BuildSeat(joel.SmallId, seat, 0, true));

        Unqueue(joel, 2);
        Unqueue(joel, 3);

        Assert.Equal(3, world.Server.Entities.Entities.Count(e => e.Discovered));
    }

    [Fact]
    public void Level_props_nobody_sat_in_still_count_toward_the_limit()
    {
        using var world = LimitOfTwo();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        Unqueue(joel, 1);
        Unqueue(joel, 2);
        Unqueue(joel, 3);

        Assert.Equal(2, world.Server.Entities.Entities.Count(e => e.Discovered));
    }

    [Fact]
    public void A_pose_past_the_cap_is_still_relayed_to_everybody_else()
    {
        using var world = LimitOfTwo();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        Pose(joel, 500);
        Pose(joel, 501);
        Pose(joel, 502);

        Assert.Contains(world.Transport.SentTo(kanza.Connection), sent =>
            sent.Message[0] == FusionProtocol.TagEntityPoseUpdate
            && FusionProtocol.TryReadEntityPose(sent.Message) is { } pose
            && pose.EntityId == 502);
    }

    [Fact]
    public void Refusing_level_props_is_logged_once_per_player_per_level()
    {
        using var world = LimitOfTwo();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        const string line = "Not tracking more level props for Joel: they already have 2";

        Pose(joel, 500);
        Pose(joel, 501);
        Pose(joel, 502);
        Pose(joel, 503);

        Assert.Equal(1, world.Server.RecentLog(2000).Count(e => e.Message == line));

        // A new level forgets every entity, so Joel can fill the cap again.
        world.Server.SetLevel("Test.Level.Other", "Other", -1, null);
        world.Sync();

        Pose(joel, 600);
        Pose(joel, 601);
        Pose(joel, 602);

        Assert.Equal(2, world.Server.RecentLog(2000).Count(e => e.Message == line));
    }
}
