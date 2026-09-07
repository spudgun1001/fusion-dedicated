using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The lobby has to publish itself again without the main loop ever waiting on
/// it. Awaiting there deadlocks the server: Steam only completes the call when
/// RunCallbacks is pumped, and after startup the main loop is the only thing
/// pumping it, so the await would wait on a callback nothing can raise.
/// </summary>
public class LobbyPublishGateTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void With_nothing_running_an_attempt_may_start()
        => Assert.True(new LobbyPublishGate().MayStart(Now));

    [Fact]
    public void Only_one_attempt_runs_at_a_time()
    {
        var gate = new LobbyPublishGate();
        gate.Started(new TaskCompletionSource<bool>().Task, Now);

        Assert.False(gate.MayStart(Now.AddSeconds(10)));
        Assert.True(gate.InFlight);
    }

    [Fact]
    public void Nothing_is_collected_while_it_is_still_running()
    {
        var gate = new LobbyPublishGate();
        gate.Started(new TaskCompletionSource<bool>().Task, Now);

        Assert.Null(gate.Collect());
    }

    [Fact]
    public void A_lobby_that_published_is_collected_once()
    {
        var gate = new LobbyPublishGate();
        gate.Started(Task.FromResult(true), Now);

        Assert.True(gate.Collect());
        Assert.Null(gate.Collect());
        Assert.False(gate.InFlight);
    }

    [Fact]
    public void An_attempt_that_failed_is_collected_as_a_failure()
    {
        var gate = new LobbyPublishGate();
        gate.Started(Task.FromResult(false), Now);

        Assert.False(gate.Collect());
    }

    [Fact]
    public void A_throw_is_a_failure_rather_than_something_that_ends_the_loop()
    {
        // Anything escaping here leaves the main loop, which ends the process.
        var gate = new LobbyPublishGate();
        gate.Started(Task.FromException<bool>(new InvalidOperationException("steam")), Now);

        Assert.False(gate.Collect());
    }

    [Fact]
    public void After_collecting_another_attempt_may_start()
    {
        var gate = new LobbyPublishGate();
        gate.Started(Task.FromResult(false), Now);
        gate.Collect();

        Assert.True(gate.MayStart(Now));
    }

    [Fact]
    public void An_attempt_that_never_answers_is_abandoned()
    {
        // Steam can simply not call back. One wedged attempt must not mean the
        // lobby is never published again for the life of the process.
        var gate = new LobbyPublishGate(TimeSpan.FromSeconds(60));
        gate.Started(new TaskCompletionSource<bool>().Task, Now);

        Assert.False(gate.MayStart(Now.AddSeconds(59)));
        Assert.True(gate.MayStart(Now.AddSeconds(61)));
    }

    [Fact]
    public void Abandoning_lets_the_next_one_be_collected()
    {
        var gate = new LobbyPublishGate(TimeSpan.FromSeconds(60));
        gate.Started(new TaskCompletionSource<bool>().Task, Now);

        Assert.True(gate.MayStart(Now.AddSeconds(61)));

        gate.Started(Task.FromResult(true), Now.AddSeconds(61));

        Assert.True(gate.Collect());
    }

    [Fact]
    public void A_lost_lobby_comes_back_without_the_loop_ever_waiting()
    {
        // The whole recovery, as the main loop runs it: the lobby goes, thirty
        // seconds pass, an attempt starts, the loop keeps turning, and a later
        // pass picks up the result.
        var retry = new LobbyRetry(TimeSpan.FromSeconds(30), Now);
        var gate = new LobbyPublishGate();
        var pending = new TaskCompletionSource<bool>();

        bool published = true;
        var now = Now;
        bool recovered = false;

        for (int pass = 0; pass < 100; pass++)
        {
            now = now.AddSeconds(1);

            if (pass == 5)
            {
                published = false;  // Steam drops it
            }

            if (gate.MayStart(now) && retry.ShouldRetry(published, now))
            {
                gate.Started(pending.Task, now);
                pending.SetResult(true);  // the callback lands on a later pass
            }

            if (gate.Collect() is { } result && result)
            {
                published = true;
                recovered = true;
            }
        }

        Assert.True(recovered);
        Assert.True(published);
    }
}
