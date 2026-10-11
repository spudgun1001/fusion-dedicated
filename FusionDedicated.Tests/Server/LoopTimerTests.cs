using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Players reported lag with no trace in the log, because nothing timed the server's own loop.
/// A slow pass now leaves one line saying where the time went.
/// </summary>
public class LoopTimerTests
{
    private static readonly DateTime Start = new(2026, 10, 11, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_quick_pass_logs_nothing()
    {
        var timer = new LoopTimer();
        timer.Phase("receive", 20);

        Assert.Null(timer.End(30, 0, 0, Start));
    }

    [Fact]
    public void A_slow_pass_names_its_phases_and_slowest_message()
    {
        var timer = new LoopTimer();
        timer.Message(17, "Dennis", 4);
        timer.Message(4, "Joel", 95);
        timer.Message(17, "Dennis", 2);
        timer.Phase("receive", 120);
        timer.Phase("tick", 40);

        var line = timer.End(182, 8, 1, Start);

        Assert.Equal("Slow loop pass: 182 ms (receive 120 ms over 3 message(s), slowest tag 4 from Joel 95 ms; " +
                     "tick 40 ms; lock wait 8 ms; 1 GC)", line);
    }

    [Fact]
    public void Each_pass_starts_from_nothing()
    {
        var timer = new LoopTimer();
        timer.Message(4, "Joel", 95);
        timer.Phase("receive", 120);
        timer.End(130, 0, 0, Start);

        timer.Phase("tick", 70);

        Assert.Equal("Slow loop pass: 75 ms (tick 70 ms; lock wait 0 ms; 0 GC)",
            timer.End(75, 0, 0, Start.AddMinutes(1)));
    }

    [Fact]
    public void Slow_passes_close_together_log_once_then_count_the_rest()
    {
        var timer = new LoopTimer();
        Assert.NotNull(timer.End(60, 0, 0, Start));
        Assert.Null(timer.End(60, 0, 0, Start.AddSeconds(2)));
        Assert.Null(timer.End(60, 0, 0, Start.AddSeconds(5)));

        Assert.EndsWith("; 2 more slow pass(es) held back", timer.End(60, 0, 0, Start.AddSeconds(11)));
    }
}
