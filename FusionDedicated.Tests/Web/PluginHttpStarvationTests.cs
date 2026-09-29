using System.Text;
using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

[CollectionDefinition(nameof(PluginHttpStarvationTests), DisableParallelization = true)]
public class PluginHttpStarvationCollection;

/// <summary>Run alone, since it ties up every thread pool thread for a moment.</summary>
[Collection(nameof(PluginHttpStarvationTests))]
public class PluginHttpStarvationTests
{
    [Fact]
    public void A_buffered_body_is_read_while_the_thread_pool_is_busy()
    {
        var http = new PluginHttp(new PluginHealth(), (_, _) => { });
        http.Handle("discord", "me", PanelRole.Viewer, _ => PluginHttpReply.Ok("{}"));

        ThreadPool.GetMinThreads(out int workers, out _);
        int queued = workers + 4;
        using var release = new ManualResetEventSlim();
        using var finished = new CountdownEvent(queued);
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
            // An item still waiting on a disposed event would throw on a pool thread and end the test host.
            release.Set();
            finished.Wait();
        }
    }
}
