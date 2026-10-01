using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class OutboundTrailTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 20, 20, 5, DateTimeKind.Utc);

    // Tag, relay ToOtherClients, unreliable channel, sender present, sender id, then a payload.
    private static byte[] Message(byte tag, byte sender, int payload = 10)
        => new byte[] { tag, 3, 1, 1, sender }.Concat(new byte[payload]).ToArray();

    [Fact]
    public void Nothing_sent_says_nothing()
        => Assert.Null(new OutboundTrail().Summary(7, Now, _ => "unknown"));

    [Fact]
    public void A_summary_counts_each_type_by_who_sent_it_and_names_the_largest()
    {
        var trail = new OutboundTrail();
        for (int i = 0; i < 3; i++) trail.Note(7, Message(4, 3), Now.AddSeconds(-1));
        trail.Note(7, Message(99, 10, payload: 5000), Now.AddSeconds(-0.5));

        string summary = trail.Summary(7, Now, tag => tag == 4 ? "PlayerPoseUpdate" : "unknown")!;

        Assert.Contains("4 messages", summary);
        Assert.Contains("tag 4 PlayerPoseUpdate from player 3: 3", summary);
        Assert.Contains("tag 99 from player 10: 1", summary);
        Assert.Contains("largest tag 99 from player 10", summary);
    }

    [Fact]
    public void Only_the_last_five_seconds_are_kept()
    {
        var trail = new OutboundTrail();
        trail.Note(7, Message(4, 3), Now.AddSeconds(-30));
        trail.Note(7, Message(5, 3), Now.AddSeconds(-2));

        string summary = trail.Summary(7, Now, _ => "unknown")!;

        Assert.Contains("1 messages", summary);
        Assert.DoesNotContain("tag 4", summary);
    }

    [Fact]
    public void A_forgotten_player_has_no_trail()
    {
        var trail = new OutboundTrail();
        trail.Note(7, Message(4, 3), Now);
        trail.Forget(7);
        Assert.Null(trail.Summary(7, Now, _ => "unknown"));
    }
}
