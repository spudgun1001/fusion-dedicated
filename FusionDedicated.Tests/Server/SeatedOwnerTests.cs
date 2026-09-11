using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A vehicle's owner follows whoever sits in it and sends its poses.
///
/// Sitting in a driver seat makes the driver the owner on every client that saw
/// the seat, and nothing is sent. The server and anybody who missed the seat kept
/// the old owner and threw the driver's poses away, so the car stuttered for them.
/// </summary>
public class SeatedOwnerTests
{
    [Fact]
    public void A_seated_pose_sender_becomes_the_owner()
        => Assert.True(WorldCatchup.OwnerFromSeatedPose(sender: 3, registryOwner: 1,
            senderSeatEntity: 300, poseEntity: 300));

    [Fact]
    public void A_seated_pose_sender_takes_a_vehicle_nobody_owns()
        => Assert.True(WorldCatchup.OwnerFromSeatedPose(3, null, 300, 300));

    [Fact]
    public void A_bystanders_pose_changes_nothing()
        => Assert.False(WorldCatchup.OwnerFromSeatedPose(3, 1, senderSeatEntity: null, poseEntity: 300));

    [Fact]
    public void The_owners_own_pose_changes_nothing()
        => Assert.False(WorldCatchup.OwnerFromSeatedPose(3, 3, 300, 300));

    [Fact]
    public void A_rider_of_a_different_vehicle_changes_nothing()
        => Assert.False(WorldCatchup.OwnerFromSeatedPose(3, 1, senderSeatEntity: 400, poseEntity: 300));

    [Fact]
    public void The_owner_is_announced_once_and_not_on_every_pose()
    {
        // The glue sets the registry owner before announcing, so the next pose
        // from the same driver is no longer a mismatch.
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Atv", 1, 0, 0, 0);

        int announced = 0;

        for (int pose = 0; pose < 5; pose++)
        {
            if (WorldCatchup.OwnerFromSeatedPose(3, registry.Get(300)!.OwnerSmallId, 300, 300))
            {
                registry.SetOwner(300, 3);
                announced++;
            }
        }

        Assert.Equal(1, announced);
        Assert.Equal((byte?)3, registry.Get(300)!.OwnerSmallId);
    }

    [Fact]
    public void The_pose_glue_sets_the_owner_then_tells_everyone()
    {
        string track = FusionServerSource.Method("private void TrackEntityPose(");

        int noted = track.IndexOf("Entities.NotePose(", StringComparison.Ordinal);
        int rule = track.IndexOf("WorldCatchup.OwnerFromSeatedPose(", StringComparison.Ordinal);
        int set = track.IndexOf("Entities.SetOwner(vehicleId, sender.SmallId);", StringComparison.Ordinal);
        int announce = track.IndexOf("AnnounceOwner(vehicleId, sender.SmallId);", StringComparison.Ordinal);

        Assert.True(noted > 0, "the pose is no longer noted here");
        Assert.True(rule > noted, "the owner rule must run after the pose is noted");
        Assert.True(set > rule && announce > set,
            "the registry owner must be set before the announcement, or every pose announces again");
    }

    [Fact]
    public void A_rider_only_takes_the_vehicle_when_they_may_hold_it()
    {
        // Last, so the rank and plugin checks only run when the owner is about to change.
        string track = FusionServerSource.Method("private void TrackEntityPose(");

        int rule = track.IndexOf("WorldCatchup.OwnerFromSeatedPose(", StringComparison.Ordinal);
        int body = track.IndexOf("\n        {", rule, StringComparison.Ordinal);

        Assert.True(rule > 0 && body > rule, "the owner-follows condition is no longer here");
        Assert.EndsWith("&& MayHold(sender, vehicleId))", track[rule..body].TrimEnd());
    }
}
