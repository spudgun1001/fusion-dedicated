using FusionDedicated;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A joiner's catch-up goes out as fast as a per-player allowance refills. The allowance
/// starts full, so a catch-up that fits in it goes out at once as it always did.
/// </summary>
public class CatchupOutboxTests
{
    private readonly List<(byte Player, byte Message, bool Reliable)> _sent = new();
    private readonly List<string> _warnings = new();
    private DateTime _now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private int _perSecond;

    private CatchupOutbox Outbox(int perSecond)
    {
        _perSecond = perSecond;

        return new CatchupOutbox(() => _perSecond, () => _now,
            (player, message, reliable) =>
            {
                _sent.Add((player.SmallId, message[0], reliable));
                return true;
            },
            warn: _warnings.Add);
    }

    private static ConnectedPlayer Player(byte smallId) => new()
    {
        Connection = new Steamworks.HSteamNetConnection(smallId),
        PlatformId = 76561198000000000UL + smallId,
        SmallId = smallId,
    };

    private static void EnqueueMany(CatchupOutbox outbox, ConnectedPlayer player, int count, int from = 0)
    {
        for (var i = from; i < from + count; i++)
        {
            outbox.Enqueue(player, new[] { (byte)i }, reliable: true);
        }
    }

    private void Wait(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    [Fact]
    public void The_default_is_a_hundred_a_second()
        => Assert.Equal(100, new ServerConfig().CatchupMessagesPerSecond);

    [Fact]
    public void A_burst_up_to_the_allowance_goes_out_at_once()
    {
        var outbox = Outbox(5);

        EnqueueMany(outbox, Player(1), 8);

        Assert.Equal(5, _sent.Count);
        Assert.Equal(3, outbox.Waiting(1));
    }

    [Fact]
    public void The_rest_goes_out_as_the_allowance_refills()
    {
        var outbox = Outbox(10);
        EnqueueMany(outbox, Player(1), 25);

        Wait(0.5);
        outbox.Pump();
        Assert.Equal(15, _sent.Count);

        Wait(1);
        outbox.Pump();
        Assert.Equal(25, _sent.Count);
    }

    [Fact]
    public void The_allowance_never_holds_more_than_one_second()
    {
        var outbox = Outbox(5);
        EnqueueMany(outbox, Player(1), 12);

        Wait(10);
        outbox.Pump();

        Assert.Equal(10, _sent.Count);
    }

    [Fact]
    public void Messages_leave_in_the_order_they_were_queued()
    {
        var outbox = Outbox(2);
        EnqueueMany(outbox, Player(1), 6);

        for (var second = 0; second < 2; second++)
        {
            Wait(1);
            outbox.Pump();
        }

        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5 }, _sent.Select(s => s.Message));
    }

    [Fact]
    public void A_new_message_waits_behind_older_ones()
    {
        var outbox = Outbox(2);
        var player = Player(1);
        EnqueueMany(outbox, player, 3);

        Wait(0.5);
        outbox.Enqueue(player, new byte[] { 3 }, reliable: true);
        Assert.Equal(new byte[] { 0, 1 }, _sent.Select(s => s.Message));

        outbox.Pump();
        Assert.Equal(new byte[] { 0, 1, 2 }, _sent.Select(s => s.Message));

        Wait(0.5);
        outbox.Pump();
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, _sent.Select(s => s.Message));
    }

    [Fact]
    public void Each_player_has_their_own_allowance()
    {
        var outbox = Outbox(2);

        EnqueueMany(outbox, Player(1), 3);
        EnqueueMany(outbox, Player(2), 2);

        Assert.Equal(2, _sent.Count(s => s.Player == 2));
    }

    [Fact]
    public void Whether_a_message_is_reliable_is_kept()
    {
        var outbox = Outbox(1);
        var player = Player(1);

        outbox.Enqueue(player, new byte[] { 0 }, reliable: false);
        outbox.Enqueue(player, new byte[] { 1 }, reliable: true);
        Wait(1);
        outbox.Pump();

        Assert.Equal(new[] { false, true }, _sent.Select(s => s.Reliable));
    }

    [Fact]
    public void Forgetting_a_player_drops_what_waits_for_them()
    {
        var outbox = Outbox(1);
        EnqueueMany(outbox, Player(1), 3);

        outbox.Forget(1);
        Wait(10);
        outbox.Pump();

        Assert.Single(_sent);
        Assert.Equal(0, outbox.Waiting(1));
    }

    [Fact]
    public void Clearing_drops_every_queue_and_fills_every_allowance()
    {
        var outbox = Outbox(1);
        var joel = Player(1);
        EnqueueMany(outbox, joel, 2);
        EnqueueMany(outbox, Player(2), 2);

        outbox.Clear();
        outbox.Enqueue(joel, new byte[] { 9 }, reliable: true);
        Wait(10);
        outbox.Pump();

        Assert.Equal(new byte[] { 0, 0, 9 }, _sent.Select(s => s.Message));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Zero_or_less_sends_everything_at_once(int perSecond)
    {
        var outbox = Outbox(perSecond);

        EnqueueMany(outbox, Player(1), 200);

        Assert.Equal(200, _sent.Count);
        Assert.Equal(0, outbox.Waiting(1));
    }

    [Fact]
    public void Turning_pacing_off_sends_what_waits_before_anything_new()
    {
        var outbox = Outbox(1);
        var player = Player(1);
        EnqueueMany(outbox, player, 3);

        _perSecond = 0;
        outbox.Enqueue(player, new byte[] { 3 }, reliable: true);

        Assert.Equal(new byte[] { 0, 1, 2, 3 }, _sent.Select(s => s.Message));
    }

    [Fact]
    public void Turning_pacing_off_lets_the_pump_send_everything()
    {
        var outbox = Outbox(1);
        EnqueueMany(outbox, Player(1), 3);

        _perSecond = 0;
        outbox.Pump();

        Assert.Equal(3, _sent.Count);
    }

    [Fact]
    public void A_backlog_is_reported_once_with_its_size()
    {
        var outbox = Outbox(2);
        var player = Player(1);
        EnqueueMany(outbox, player, 5);

        Assert.Equal(3, outbox.BacklogToReport(1));
        Assert.Equal(0, outbox.BacklogToReport(1));

        EnqueueMany(outbox, player, 4, from: 5);
        Assert.Equal(0, outbox.BacklogToReport(1));
    }

    [Fact]
    public void Nothing_waiting_is_not_a_backlog()
    {
        var outbox = Outbox(5);
        EnqueueMany(outbox, Player(1), 3);

        Assert.Equal(0, outbox.BacklogToReport(1));
    }

    [Fact]
    public void A_backlog_is_reported_again_after_a_clear()
    {
        var outbox = Outbox(1);
        var player = Player(1);

        EnqueueMany(outbox, player, 3);
        Assert.Equal(2, outbox.BacklogToReport(1));

        outbox.Clear();
        EnqueueMany(outbox, player, 3);
        Assert.Equal(2, outbox.BacklogToReport(1));
    }

    [Fact]
    public void A_null_build_spends_no_token_and_the_next_item_still_goes_out()
    {
        var outbox = Outbox(1);
        var player = Player(1);

        outbox.Enqueue(player, new byte[] { 0 }, reliable: true);
        outbox.Enqueue(player, () => null, reliable: true);
        outbox.Enqueue(player, new byte[] { 2 }, reliable: true);

        Assert.Single(_sent);

        Wait(1);
        outbox.Pump();

        Assert.Equal(new byte[] { 0, 2 }, _sent.Select(s => s.Message));
        Assert.Equal(0, outbox.Waiting(1));
    }

    [Fact]
    public void A_builder_runs_at_send_time_not_at_enqueue_time()
    {
        var outbox = Outbox(1);
        var player = Player(1);
        byte value = 1;

        outbox.Enqueue(player, new byte[] { 0 }, reliable: true);
        outbox.Enqueue(player, () => new[] { value }, reliable: true);

        value = 9;

        Wait(1);
        outbox.Pump();

        Assert.Equal(new byte[] { 0, 9 }, _sent.Select(s => s.Message));
    }

    [Fact]
    public void A_builder_that_throws_costs_no_token_and_is_reported_as_a_warning()
    {
        var outbox = Outbox(1);
        var player = Player(1);

        outbox.Enqueue(player, new byte[] { 0 }, reliable: true);
        outbox.Enqueue(player, () => throw new InvalidOperationException("boom"), reliable: true);
        outbox.Enqueue(player, new byte[] { 2 }, reliable: true);

        Wait(1);
        outbox.Pump();

        Assert.Equal(new byte[] { 0, 2 }, _sent.Select(s => s.Message));
        Assert.Single(_warnings);
    }

    [Fact]
    public void A_builder_that_forgets_another_players_lane_during_pump_does_not_throw()
    {
        var outbox = Outbox(1);
        var p1 = Player(1);
        var p2 = Player(2);

        outbox.Enqueue(p1, new byte[] { 0 }, reliable: true);
        outbox.Enqueue(p1, () => { outbox.Forget(2); return new byte[] { 1 }; }, reliable: true);

        outbox.Enqueue(p2, new byte[] { 2 }, reliable: true);
        outbox.Enqueue(p2, new byte[] { 3 }, reliable: true);

        Wait(1);
        var exception = Record.Exception(() => outbox.Pump());

        Assert.Null(exception);
        Assert.DoesNotContain(_sent, s => s.Player == 2 && s.Message == 3);
    }
}
