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

        Assert.True(ran.Wait(TimeSpan.FromSeconds(5)));
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

        Assert.True(ran.Wait(TimeSpan.FromSeconds(5)));

        // Give the catch a moment to write its line.
        Thread.Sleep(200);
        context.StopTimers();

        Assert.Contains(log, l => l.StartsWith("WARN") && l.Contains("threw and was skipped"));
    }

    [Fact]
    public void It_keeps_going_after_a_throw()
    {
        // A payday that fails once should not stop every payday after it.
        var (context, _) = Build();
        int runs = 0;

        context.Every(TimeSpan.FromSeconds(1), () =>
        {
            Interlocked.Increment(ref runs);
            throw new InvalidOperationException("no");
        });

        Thread.Sleep(TimeSpan.FromSeconds(2.5));
        context.StopTimers();

        Assert.True(runs >= 2, $"ran {runs} times");
    }

    [Fact]
    public void Stopping_means_it_stops()
    {
        // A timer left running would fire into an unloaded assembly, which ends
        // the process rather than throwing.
        var (context, _) = Build();
        int runs = 0;

        context.Every(TimeSpan.FromSeconds(1), () => Interlocked.Increment(ref runs));

        Thread.Sleep(TimeSpan.FromSeconds(1.5));
        context.StopTimers();

        int after = runs;
        Thread.Sleep(TimeSpan.FromSeconds(1.5));

        Assert.Equal(after, runs);
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

        context.Every(TimeSpan.FromMilliseconds(1), () => Interlocked.Increment(ref runs));

        Thread.Sleep(TimeSpan.FromSeconds(1.5));
        context.StopTimers();

        Assert.InRange(runs, 1, 3);
    }
}
