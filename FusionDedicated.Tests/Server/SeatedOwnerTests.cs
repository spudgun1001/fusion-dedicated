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
    public void The_pose_glue_sets_the_owner_before_judging_whose_pose_it_is()
    {
        // The owner-gate fix needs the seated driver to become the owner before
        // the pose is weighed against the registry, or their own first pose as
        // driver would be thrown away as a non-owner's.
        string track = FusionServerSource.Method("private void TrackEntityPose(");

        int rule = track.IndexOf("WorldCatchup.OwnerFromSeatedPose(", StringComparison.Ordinal);
        int noted = track.IndexOf("Entities.NotePose(", StringComparison.Ordinal);
        int set = track.IndexOf("Entities.SetOwner(vehicleId, sender.SmallId);", StringComparison.Ordinal);
        int announce = track.IndexOf("AnnounceOwner(vehicleId, sender.SmallId);", StringComparison.Ordinal);

        Assert.True(rule > 0, "the owner rule is no longer here");
        Assert.True(noted > rule, "the pose must be noted after the owner rule, not before");
        Assert.True(set > rule && announce > set,
            "the registry owner must be set before the announcement, or every pose announces again");
    }

    [Fact]
    public void A_rider_only_takes_the_vehicle_when_they_may_hold_it()
    {
        // MayHold runs inside the owner-follows block, so the rank and plugin
        // checks only happen when the owner is about to change. MayHold can
        // remove the entity, so a refusal must return before the pose below is
        // ever noted.
        string track = FusionServerSource.Method("private void TrackEntityPose(");

        int rule = track.IndexOf("WorldCatchup.OwnerFromSeatedPose(", StringComparison.Ordinal);
        int body = track.IndexOf("\n        {", rule, StringComparison.Ordinal);
        int mayHold = track.IndexOf("MayHold(sender, vehicleId)", body, StringComparison.Ordinal);
        int set = track.IndexOf("Entities.SetOwner(vehicleId, sender.SmallId);", StringComparison.Ordinal);
        int noted = track.IndexOf("Entities.NotePose(", StringComparison.Ordinal);

        Assert.True(rule > 0 && body > rule, "the owner-follows condition is no longer here");
        Assert.DoesNotContain("MayHold", track[rule..body]);

        Assert.True(mayHold > body && mayHold < set,
            "MayHold must run inside the owner-follows block, before the owner is set");

        int refusalReturn = track.IndexOf("return;", mayHold, StringComparison.Ordinal);
        Assert.True(refusalReturn > mayHold && refusalReturn < set && refusalReturn < noted,
            "a MayHold refusal must return before the owner is set and before the pose is noted");
    }
}
