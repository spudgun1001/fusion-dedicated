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
        using var release = new ManualResetEventSlim();
        int running = 0;

        for (int i = 0; i < workers + 4; i++)
        {
            ThreadPool.QueueUserWorkItem(_ => { Interlocked.Increment(ref running); release.Wait(); });
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
            release.Set();
        }
    }
}
