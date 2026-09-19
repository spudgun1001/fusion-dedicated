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
        Assert.Contains("grab.Message", replay);
    }
}
