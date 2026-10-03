using System.Net;
using System.Net.Sockets;
using System.Text;
using FusionDedicated.Plugins;
using FusionDedicated.Tests.Harness;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>The real panel on a loopback port, with a caller that promises a body and stalls. Run alone so its timing is its own.</summary>
[Collection(RunsAlone.Name)]
public class PluginHttpLoopbackTests
{
    private const string Password = "pw";

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>A port can be taken between probing it and binding it, so a failed bind tries another.</summary>
    private static Dashboard StartPanel(World world, ServerConfig config, PluginHttp http)
    {
        for (int attempt = 1; ; attempt++)
        {
            config.DashboardPort = FreePort();

            // The lobby is only read by /api/state.
            var dashboard = new Dashboard(world.Server, config, null!) { PluginHttp = http, PluginBodyLimit = TimeSpan.FromMilliseconds(300) };

            try
            {
                dashboard.Start();
                return dashboard;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
            }
        }
    }

    private static string Auth => Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:" + Password));

    [Fact]
    public async Task A_stalled_body_is_dropped_and_the_next_request_is_answered()
    {
        var config = new ServerConfig { CullOrphanedEntities = false, DashboardHost = "localhost", DashboardPassword = Password };
        using var world = new World(config);

        var http = new PluginHttp(new PluginHealth(), (_, _) => { });
        var bodies = new System.Collections.Concurrent.ConcurrentQueue<string>();
        http.Handle("discord", "me", PanelRole.Viewer, r =>
        {
            bodies.Enqueue(r.Body);
            return PluginHttpReply.Ok("{\"body\":" + System.Text.Json.JsonSerializer.Serialize(r.Body) + "}");
        });

        var dashboard = StartPanel(world, config, http);
        int port = config.DashboardPort;
        Assert.True(dashboard.IsListening);

        try
        {
            using var stalled = new TcpClient();
            stalled.Connect("localhost", port);
            var stream = stalled.GetStream();
            stream.ReadTimeout = 5000;

            byte[] head = Encoding.ASCII.GetBytes(
                $"POST /api/plugins/http/discord/me HTTP/1.1\r\nHost: localhost:{port}\r\nAuthorization: Basic {Auth}\r\n" +
                "Content-Type: application/json\r\nContent-Length: 16000\r\n\r\n{\"a\":");
            stream.Write(head);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            string answer = "";
            try
            {
                var buffer = new byte[4096];
                int n = stream.Read(buffer);
                answer = Encoding.ASCII.GetString(buffer, 0, n);
            }
            catch (IOException)
            {
                // Aborted.
            }
            clock.Stop();

            // Windows drops it with nothing sent. Linux's managed listener writes the status line as it aborts.
            Assert.True(answer == "" || answer.StartsWith("HTTP/1.1 408 ", StringComparison.Ordinal), answer);
            Assert.DoesNotContain("body", answer);
            Assert.Empty(bodies);
            Assert.InRange(clock.ElapsedMilliseconds, 200, 3000);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Auth);
            client.Timeout = TimeSpan.FromSeconds(5);

            var reply = await client.PostAsync($"http://localhost:{port}/api/plugins/http/discord/me",
                new StringContent("{\"a\":\"b\"}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            Assert.Contains("\\u0022a\\u0022", await reply.Content.ReadAsStringAsync());
        }
        finally
        {
            dashboard.Stop();
        }
    }
}
