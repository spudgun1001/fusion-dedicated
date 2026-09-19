using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Telling a client what is held in an entity it has just built. Fusion asks the
/// holder instead, and when that answer is lost the item floats for the joiner.
/// </summary>
public class GrabReplayTests
{
    private static readonly HeldItem RightOfThree = new(3, 2, 300, new byte[] { 9 });
    private static readonly HeldItem LeftOfFour = new(4, 1, 300, new byte[] { 9 });
    private static readonly HeldItem Elsewhere = new(5, 2, 400, new byte[] { 9 });
    private static readonly HeldItem Unrecorded = new(6, 2, 300);
    private static readonly HeldItem AlsoOfThree = new(3, 1, 310, new byte[] { 9 });

    private static bool Everyone(byte smallId) => true;

    [Fact]
    public void Every_hand_on_that_entity_is_replayed()
        => Assert.Equal(new[] { RightOfThree, LeftOfFour },
            WorldCatchup.GrabsToReplay(new[] { RightOfThree, Elsewhere, LeftOfFour }, 300, 9, Everyone));

    [Fact]
    public void The_one_asking_is_not_given_back_its_own_grab()
        => Assert.Equal(new[] { RightOfThree },
            WorldCatchup.GrabsToReplay(new[] { RightOfThree, LeftOfFour }, 300, requester: 4, Everyone));

    [Fact]
    public void A_holder_who_has_left_is_not_replayed()
        => Assert.Equal(new[] { LeftOfFour },
            WorldCatchup.GrabsToReplay(new[] { RightOfThree, LeftOfFour }, 300, 9, present: id => id != 3));

    [Fact]
    public void A_hold_with_no_grab_kept_is_not_replayed()
        => Assert.Empty(WorldCatchup.GrabsToReplay(new[] { Unrecorded }, 300, 9, Everyone));

    [Fact]
    public void An_entity_nobody_holds_replays_nothing()
        => Assert.Empty(WorldCatchup.GrabsToReplay(new[] { Elsewhere }, 300, 9, Everyone));

    [Fact]
    public void A_data_request_queues_the_grabs_the_holders_sent()
    {
        Assert.Contains("ReplayGrabs(sender, request.EntityId);",
            FusionServerSource.Method("private void HandleEntityDataRequest("));

        string replay = FusionServerSource.Method("private void ReplayGrabs(");

        Assert.Contains("_catchup.Enqueue(requester", replay);
        Assert.Contains("_grabs.Message(grab.Player, grab.Hand, grab.EntityId)", replay);
    }

    [Fact]
    public void Both_hands_of_one_player_are_replayed_when_their_rig_is_asked_about()
        => Assert.Equal(new[] { RightOfThree, AlsoOfThree },
            WorldCatchup.HandsToReplay(new[] { RightOfThree, LeftOfFour, AlsoOfThree }, 3, 9, Everyone));

    [Fact]
    public void A_rig_request_from_the_holder_replays_nothing()
        => Assert.Empty(WorldCatchup.HandsToReplay(new[] { RightOfThree }, 3, requester: 3, Everyone));

    [Fact]
    public void A_prop_the_server_has_is_a_grab_worth_recording()
        => Assert.True(WorldCatchup.KnownGrabTarget(300, id => true, id => false));

    [Fact]
    public void An_id_the_server_never_registered_is_not()
        => Assert.False(WorldCatchup.KnownGrabTarget(300, id => false, id => true));

    [Fact]
    public void Holding_somebody_who_is_here_is_recorded_though_no_entity_has_that_id()
        => Assert.True(WorldCatchup.KnownGrabTarget(4, id => false, id => true));

    [Fact]
    public void Holding_somebody_who_has_left_is_not()
        => Assert.False(WorldCatchup.KnownGrabTarget(4, id => true, id => false));
}
