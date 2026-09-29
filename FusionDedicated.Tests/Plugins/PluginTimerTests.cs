using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// A plugin's repeating work must not be able to end the server.
///
/// LabRP's payday ran on a raw Timer. When its callback threw, the exception had
/// nowhere to go: .NET ends the process for an unhandled exception on a thread
/// pool thread, and a live server went down mid-session with players on it.
/// </summary>
public class PluginTimerTests
{
    private sealed class NoActions : IPluginActions
    {
        public void Kick(ulong platformId, string reason) { }
        public void Ban(ulong platformId, string reason) { }
        public void SetRank(ulong platformId, PermissionLevel level) { }
        public void Despawn(ushort entityId) { }
        public void SendModule(ulong platformId, long handlerTag, byte[] payload) { }
        public void BroadcastModule(long handlerTag, byte[] payload) { }
    }

    private static (PluginContext Context, List<string> Log) Build()
    {
        var log = new List<string>();
        void Write(string level, string message) => log.Add($"{level} {message}");

        var health = new PluginHealth();

        var context = new PluginContext(
            "labrp", new PluginEvents(health, Write),
            new PluginStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new PluginPanel(health, Write), new PluginModules(health, Write),
            new NoActions(), () => Array.Empty<PluginPlayer>(), Write);

        return (context, log);
    }

    [Fact]
    public void Repeating_work_runs()
    {
        var (context, _) = Build();
        using var ran = new ManualResetEventSlim();

        context.Every(TimeSpan.FromSeconds(1), () => ran.Set());

        Assert.True(ran.Wait(TimeSpan.FromSeconds(10)));
        context.StopTimers();
    }

    [Fact]
    public void Work_that_throws_is_logged_rather_than_ending_the_process()
    {
        var (context, log) = Build();
        using var ran = new ManualResetEventSlim();

        context.Every(TimeSpan.FromSeconds(1), () =>
        {
            ran.Set();
            throw new MissingMethodException(
                "Method not found: 'Void FusionDedicated.Plugins.PluginStore.Save()'.");
        });

        Assert.True(ran.Wait(TimeSpan.FromSeconds(10)));

        // StopTimers waits for the running job, so the catch has written its line.
        context.StopTimers();

        Assert.Contains(log, l => l.StartsWith("WARN") && l.Contains("threw and was skipped"));
    }

    [Fact]
    public void It_keeps_going_after_a_throw()
    {
        // A payday that fails once should not stop every payday after it.
        var (context, _) = Build();
        int runs = 0;
        using var twice = new ManualResetEventSlim();

        context.Every(TimeSpan.FromSeconds(1), () =>
        {
            if (Interlocked.Increment(ref runs) == 2)
            {
                twice.Set();
            }

            throw new InvalidOperationException("no");
        });

        bool ranTwice = twice.Wait(TimeSpan.FromSeconds(10));
        context.StopTimers();

        Assert.True(ranTwice, $"ran {Volatile.Read(ref runs)} times");
    }

    [Fact]
    public void Stopping_means_it_stops()
    {
        // A timer left running would fire into an unloaded assembly, which ends
        // the process rather than throwing.
        var (context, _) = Build();
        int runs = 0;
        using var ran = new ManualResetEventSlim();

        context.Every(TimeSpan.FromSeconds(1), () =>
        {
            Interlocked.Increment(ref runs);
            ran.Set();
        });

        Assert.True(ran.Wait(TimeSpan.FromSeconds(10)));
        context.StopTimers();

        int after = Volatile.Read(ref runs);
        Thread.Sleep(TimeSpan.FromSeconds(1.5));

        Assert.Equal(after, Volatile.Read(ref runs));
    }

    [Fact]
    public void A_job_that_stops_its_own_timers_does_not_hang()
    {
        // The job cannot finish until StopTimers returns, so waiting for it
        // would wait on itself. The bound is what lets it through.
        var (context, _) = Build();
        using var stopped = new ManualResetEventSlim();

        context.Every(TimeSpan.FromSeconds(1), () =>
        {
            context.StopTimers(TimeSpan.FromMilliseconds(200));
            stopped.Set();
        });

        Assert.True(stopped.Wait(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Stopping_twice_is_harmless()
    {
        var (context, _) = Build();
        context.Every(TimeSpan.FromSeconds(1), () => { });

        context.StopTimers();
        context.StopTimers();
    }

    [Fact]
    public void A_period_below_a_second_is_held_to_one()
    {
        // Nothing a plugin does on a repeat needs to run faster than this, and a
        // plugin asking for milliseconds would be a busy loop on the server.
        var (context, _) = Build();
        int runs = 0;
        using var ran = new ManualResetEventSlim();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        context.Every(TimeSpan.FromMilliseconds(1), () =>
        {
            Interlocked.Increment(ref runs);
            ran.Set();
        });

        Assert.True(ran.Wait(TimeSpan.FromSeconds(10)));
        Thread.Sleep(TimeSpan.FromSeconds(0.5));
        context.StopTimers();

        // At one a second it runs at most once per second waited, with room for
        // rounding and a late callback. A 1 ms period would run hundreds of times.
        Assert.InRange(Volatile.Read(ref runs), 1, (int)clock.Elapsed.TotalSeconds + 2);
    }
}
