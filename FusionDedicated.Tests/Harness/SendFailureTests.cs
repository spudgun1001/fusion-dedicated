using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Sends Steam refuses are counted apart from sent ones and summed up in the log at most once a minute.</summary>
public class SendFailureTests
{
    private const ulong JoelId = 76561198000000001;

    [Fact]
    public void A_refused_send_is_counted_and_not_added_to_the_sent_counters()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        long packets = world.Server.PacketsOut;
        long bytes = world.Server.BytesOut;

        world.Transport.FailSendsWith = "k_EResultNoConnection";
        bool sent = world.Server.SendTo(joel.Connection, new byte[] { 1, 2, 3 }, reliable: true);

        Assert.False(sent);
        Assert.Equal(1L, world.Server.SendsRefused);
        Assert.Equal(packets, world.Server.PacketsOut);
        Assert.Equal(bytes, world.Server.BytesOut);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refused_sends_are_summed_up_whether_culling_is_on_or_off(bool cull)
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = cull });
        var joel = world.Join(JoelId, "Joel");

        world.Transport.FailSendsWith = "k_EResultNoConnection";
        world.Server.SendTo(joel.Connection, new byte[] { 1 }, reliable: true);
        world.Transport.FailSendsWith = null;

        world.Tick();

        Assert.Contains(world.Server.RecentLog(2000), e => e.Message ==
            "1 sends refused by Steam in the last minute, last: k_EResultNoConnection to Joel");
    }

    [Fact]
    public void Refused_sends_are_summed_up_at_most_once_a_minute()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");

        world.Transport.FailSendsWith = "k_EResultNoConnection";
        world.Server.SendTo(joel.Connection, new byte[] { 1 }, reliable: true);
        world.Transport.FailSendsWith = null;

        world.Tick();

        Assert.Single(world.Server.RecentLog(2000), e => e.Message ==
            "1 sends refused by Steam in the last minute, last: k_EResultNoConnection to Joel");

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.SendTo(joel.Connection, new byte[] { 1 }, reliable: true);
        world.Transport.FailSendsWith = null;

        world.Advance(TimeSpan.FromSeconds(30));
        world.Tick();

        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.Contains("k_EResultLimitExceeded"));

        world.Advance(TimeSpan.FromSeconds(30));
        world.Tick();

        Assert.Contains(world.Server.RecentLog(2000), e => e.Message ==
            "1 sends refused by Steam in the last minute, last: k_EResultLimitExceeded to Joel");
    }

    [Fact]
    public void A_refused_send_to_a_connection_nobody_joined_on_names_the_connection()
    {
        using var world = new World();
        world.Transport.FailSendsWith = "k_EResultInvalidParam";

        world.Server.SendTo(new Steamworks.HSteamNetConnection(7), new byte[] { 1 }, reliable: true);
        world.Tick();

        Assert.Contains(world.Server.RecentLog(2000), e => e.Message ==
            "1 sends refused by Steam in the last minute, last: k_EResultInvalidParam to conn 7");
    }
}
