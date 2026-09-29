using System.Text;
using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>Tests that must not share the machine with any other test.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class RunsAlone
{
    public const string Name = "Runs alone";
}

/// <summary>Run alone, since it ties up every thread pool thread for a moment.</summary>
[Collection(RunsAlone.Name)]
public class PluginHttpStarvationTests
{
    [Fact]
    public void A_buffered_body_is_read_while_the_thread_pool_is_busy()
    {
        var http = new PluginHttp(new PluginHealth(), (_, _) => { });
        http.Handle("discord", "me", PanelRole.Viewer, _ => PluginHttpReply.Ok("{}"));

        ThreadPool.GetMinThreads(out int workers, out _);
        int queued = workers + 4;
        var release = new ManualResetEventSlim();
        var finished = new CountdownEvent(queued);
        int running = 0;

        for (int i = 0; i < queued; i++)
        {
            ThreadPool.QueueUserWorkItem(_ => { Interlocked.Increment(ref running); release.Wait(); finished.Signal(); });
        }

        try
        {
            SpinWait.SpinUntil(() => Volatile.Read(ref running) >= workers, 2000);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var reply = PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "POST", "/api/plugins/http/discord/me",
                new Dictionary<string, string>(), 2, new MemoryStream(Encoding.UTF8.GetBytes("{}")), TimeSpan.FromMilliseconds(300));

            Assert.Equal(200, reply.Status);
            Assert.InRange(clock.ElapsedMilliseconds, 0, 250);
        }
        finally
        {
            // An item still waiting on a disposed event would throw on a pool thread and end the test host,
            // so the events are only disposed once every item has finished. On a timeout the GC has them.
            release.Set();
            Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), "the thread pool never ran every queued item");
            release.Dispose();
            finished.Dispose();
        }
    }
}
