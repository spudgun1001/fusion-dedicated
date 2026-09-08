namespace FusionDedicated.Tests.Server;

/// <summary>
/// When a plugin is told somebody joined.
///
/// It used to be told before the player was put in the register, so a plugin
/// answering the event could neither see the person who had just joined nor send
/// them anything: a broadcast walks the register and they were not in it. LabRP
/// sends a player their balance that way, so the one person who never received it
/// was the person who had just arrived. Their wrist HUD stayed empty until
/// somebody else joined behind them, which sent a fresh copy to everybody already
/// there.
///
/// Read from the source because the handshake needs live Steam connections, and an
/// ordering with no seam to test through is still worth pinning.
/// </summary>
public class JoinOrderTests
{
    private static string JoinHandshake()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "FusionDedicated", "Server", "FusionServer.cs"));

        int start = source.IndexOf("private void HandleConnectionRequest", StringComparison.Ordinal);

        Assert.True(start > 0, "the join handshake moved");

        int end = source.IndexOf("// ---- world bookkeeping ----", start, StringComparison.Ordinal);

        Assert.True(end > start, "the end of the join handshake moved");

        return source[start..end];
    }

    [Fact]
    public void A_plugin_hears_about_a_join_after_the_player_is_registered()
    {
        string join = JoinHandshake();

        int added = join.IndexOf("Players.Add(player)", StringComparison.Ordinal);
        int raised = join.IndexOf("Plugins?.Joined.Raise", StringComparison.Ordinal);

        Assert.True(added > 0, "the player is no longer added here");
        Assert.True(raised > 0, "the joined event is no longer raised here");
        Assert.True(raised > added,
            "a plugin told about a join before the register knows about it cannot " +
            "send that player anything");
    }

    [Fact]
    public void It_is_also_after_the_newcomer_has_been_told_it_is_in()
    {
        // A plugin's first message to somebody who has not yet had the connection
        // response is one their client has nowhere to put.
        string join = JoinHandshake();

        int response = join.IndexOf("WriteConnectionResponse", StringComparison.Ordinal);
        int raised = join.IndexOf("Plugins?.Joined.Raise", StringComparison.Ordinal);

        Assert.True(raised > response);
    }

    [Fact]
    public void The_veto_still_comes_first()
    {
        // Joining is the one that may refuse, so it has to run before a slot is
        // allocated and before any of this.
        string join = JoinHandshake();

        int joining = join.IndexOf("Plugins?.Joining.Raise", StringComparison.Ordinal);
        int added = join.IndexOf("Players.Add(player)", StringComparison.Ordinal);

        Assert.True(joining > 0 && joining < added);
    }
}
