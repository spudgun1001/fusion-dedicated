namespace FusionDedicated.Tests.Server;

/// <summary>
/// Which sends go through the catch-up outbox and which go straight out. Read from the
/// source like CatchupGlueTests, since the routing has no seam of its own.
/// </summary>
public class CatchupPacingGlueTests
{
    private static string Method(string declaration) => FusionServerSource.Method(declaration);

    [Theory]
    [InlineData("private int SendWorldCatchup(")]
    [InlineData("private int SendSceneProps(")]
    [InlineData("private int SendConstraints(")]
    [InlineData("private int SendAttachments(")]
    public void Catch_up_to_a_joiner_goes_through_the_outbox(string declaration)
    {
        string method = Method(declaration);

        Assert.Contains("_catchup.Enqueue(player,", method);
        Assert.DoesNotContain("SendTo(", method);
    }

    [Fact]
    public void Level_variables_and_their_own_props_values_are_paced()
    {
        Assert.Contains("SendRpcVariable(player, tag, from, body, paced: true);", Method("private int SendRpcVariables("));
        Assert.Contains("ReplayVariables(player, entityId, paced: true);", Method("private void ResendOwnVariables("));
    }

    [Fact]
    public void A_data_request_is_answered_straight_away()
    {
        // The joiner only asks once the entity exists, so the reply is never held back.
        Assert.Contains("SendTo(requester.Connection, FusionProtocol.BuildSeat(", Method("private void ReplaySeats("));
    }

    [Fact]
    public void The_outbox_is_pumped_with_deferred_work()
        => Assert.Contains("_catchup.Pump();", Method("public void PumpDeferred("));

    [Fact]
    public void A_player_who_leaves_is_forgotten()
        => Assert.Contains("_catchup.Forget(player.SmallId);", Method("private void Depart("));

    [Fact]
    public void A_level_change_clears_the_outbox_before_the_new_level_is_loaded()
    {
        string method = Method("public void SetLevel(");

        int clear = method.IndexOf("_catchup.Clear();", StringComparison.Ordinal);
        int load = method.IndexOf("ServerProtocol.WriteSceneLoad(", StringComparison.Ordinal);

        Assert.True(clear > 0, "not cleared");
        Assert.True(load > clear, "the old level's queues must go before players are sent the new one");
    }
}
