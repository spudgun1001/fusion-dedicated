using System.Net;
using System.Net.Sockets;
using System.Text;
using FusionDedicated.Plugins;
using FusionDedicated.Tests.Harness;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>The real panel on a loopback port, with a caller that promises a body and stalls.</summary>
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

    private static string Auth => Convert.ToBase64String(Encoding.ASCII.GetBytes("admin:" + Password));

    [Fact]
    public void A_stalled_body_is_dropped_and_the_next_request_is_answered()
    {
        int port = FreePort();
        var config = new ServerConfig { CullOrphanedEntities = false, DashboardHost = "localhost", DashboardPort = port, DashboardPassword = Password };
        using var world = new World(config);

        var http = new PluginHttp(new PluginHealth(), (_, _) => { });
        http.Handle("discord", "me", PanelRole.Viewer, r => PluginHttpReply.Ok("{\"body\":" + System.Text.Json.JsonSerializer.Serialize(r.Body) + "}"));

        // The lobby is only read by /api/state.
        var dashboard = new Dashboard(world.Server, config, null!) { PluginHttp = http, PluginBodyLimit = TimeSpan.FromMilliseconds(300) };
        dashboard.Start();
        Assert.True(dashboard.IsListening);

        try
        {
            using var stalled = new TcpClient();
            stalled.Connect(IPAddress.Loopback, port);
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

            Assert.Equal("", answer);
            Assert.InRange(clock.ElapsedMilliseconds, 200, 3000);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Auth);
            client.Timeout = TimeSpan.FromSeconds(5);

            var reply = client.PostAsync($"http://localhost:{port}/api/plugins/http/discord/me",
                new StringContent("{\"a\":\"b\"}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();

            Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
            Assert.Contains("\\u0022a\\u0022", reply.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }
        finally
        {
            dashboard.Stop();
        }
    }
}
