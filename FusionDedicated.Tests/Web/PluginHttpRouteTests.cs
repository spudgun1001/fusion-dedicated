using System.Text;
using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class PluginHttpRouteTests
{
    [Theory]
    [InlineData(PanelRole.Viewer)]
    [InlineData(PanelRole.Moderator)]
    [InlineData(PanelRole.Owner)]
    [InlineData(PanelRole.Banker)]
    public void Every_signed_in_role_gets_as_far_as_the_route_check(PanelRole role)
        => Assert.True(PanelPermissions.Allows(role, "/api/plugins/http/discord/me"));

    [Fact]
    public void A_banker_route_admits_bankers_and_owners_only()
    {
        Assert.True(PanelPermissions.MayCall(PanelRole.Banker, PanelRole.Banker));
        Assert.True(PanelPermissions.MayCall(PanelRole.Owner, PanelRole.Banker));
        Assert.False(PanelPermissions.MayCall(PanelRole.Moderator, PanelRole.Banker));
        Assert.False(PanelPermissions.MayCall(PanelRole.Viewer, PanelRole.Banker));
    }

    [Fact]
    public void A_ladder_route_never_admits_a_banker()
    {
        Assert.False(PanelPermissions.MayCall(PanelRole.Banker, PanelRole.Moderator));
        Assert.True(PanelPermissions.MayCall(PanelRole.Owner, PanelRole.Moderator));
        Assert.False(PanelPermissions.MayCall(PanelRole.Viewer, PanelRole.Moderator));
    }

    [Fact]
    public void The_http_prefix_did_not_open_other_unlisted_routes()
        => Assert.False(PanelPermissions.Allows(PanelRole.Owner, "/api/plugins/httpx"));

    [Fact]
    public void The_panel_wires_the_gate_into_its_dispatch()
        => Assert.Contains("PluginHttpGate.Handle(", DashboardSource.Text());

    private const string Path = "/api/plugins/http/discord/me";

    private static readonly Dictionary<string, string> NoQuery = new();

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private static PluginHttp Registry() => new(new PluginHealth(), (_, _) => { });

    [Fact]
    public void A_null_registry_gives_404()
    {
        var reply = PluginHttpGate.Handle(null, PanelRole.Owner, "acting", "GET", Path, NoQuery, 0, new MemoryStream(), Limit);

        Assert.Equal(404, reply.Status);
        Assert.Contains("No such route", reply.Json);
    }

    [Fact]
    public void An_unknown_route_gives_404()
    {
        var reply = PluginHttpGate.Handle(Registry(), PanelRole.Owner, "acting", "GET", Path, NoQuery, 0, new MemoryStream(), Limit);

        Assert.Equal(404, reply.Status);
        Assert.Contains("No such route", reply.Json);
    }

    [Theory]
    [InlineData(PanelRole.Viewer)]
    [InlineData(PanelRole.Moderator)]
    public void A_role_below_a_banker_route_is_refused_before_the_handler_runs(PanelRole role)
    {
        var http = Registry();
        bool called = false;
        http.Handle("discord", "me", PanelRole.Banker, _ => { called = true; return PluginHttpReply.Ok("{}"); });

        var reply = PluginHttpGate.Handle(http, role, "acting", "GET", Path, NoQuery, 0, new MemoryStream(), Limit);

        Assert.Equal(403, reply.Status);
        Assert.Contains("not allowed", reply.Json);
        Assert.False(called);
    }

    [Theory]
    [InlineData(PanelRole.Banker)]
    [InlineData(PanelRole.Owner)]
    public void A_banker_route_admits_bankers_and_owners_and_the_handler_sees_the_request(PanelRole role)
    {
        var http = Registry();
        PluginHttpRequest? seen = null;
        http.Handle("discord", "me", PanelRole.Banker, req => { seen = req; return PluginHttpReply.Ok("{}"); });

        var query = new Dictionary<string, string> { ["id"] = "42" };
        var body = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        PluginHttpGate.Handle(http, role, "player:1", "POST", Path, query, 5, body, Limit);

        Assert.NotNull(seen);
        Assert.Equal("player:1", seen!.Actor);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("me", seen.Path);
        Assert.Equal("42", seen.Query["id"]);
        Assert.Equal("hello", seen.Body);
    }

    [Fact]
    public void The_handlers_own_status_and_json_pass_through_unchanged()
    {
        var http = Registry();
        http.Handle("discord", "me", PanelRole.Viewer, _ => new PluginHttpReply(429, "{\"error\":\"slow down\"}"));

        var reply = PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "GET", Path, NoQuery, 0, new MemoryStream(), Limit);

        Assert.Equal(429, reply.Status);
        Assert.Equal("{\"error\":\"slow down\"}", reply.Json);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("the body must not be read");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A caller that promised a body and never sends it.</summary>
    private sealed class StalledStream : Stream
    {
        public readonly ManualResetEventSlim Release = new();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { Release.Wait(); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void A_stalled_body_gives_408_and_asks_for_an_abort_without_running_the_handler()
    {
        var http = Registry();
        bool called = false;
        http.Handle("discord", "me", PanelRole.Viewer, _ => { called = true; return PluginHttpReply.Ok("{}"); });
        var body = new StalledStream();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var reply = PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "POST", Path, NoQuery,
            PluginHttpGate.MaxBody, body, TimeSpan.FromMilliseconds(200));

        clock.Stop();
        body.Release.Set();

        Assert.Equal(408, reply.Status);
        Assert.Equal("{\"error\":\"Too slow\"}", reply.Json);
        Assert.True(reply.Abort);
        Assert.False(called);
        Assert.InRange(clock.ElapsedMilliseconds, 150, 2000);
    }

    [Fact]
    public void An_ordinary_reply_does_not_ask_for_an_abort()
    {
        var http = Registry();
        http.Handle("discord", "me", PanelRole.Viewer, _ => new PluginHttpReply(408, "{}"));

        Assert.False(PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "GET", Path, NoQuery, 0, new MemoryStream(), Limit).Abort);
    }

    [Fact]
    public void The_panel_aborts_a_connection_the_gate_gave_up_on()
        => Assert.Contains("context.Response.Abort()", DashboardSource.Text());

    [Fact]
    public void A_content_length_over_the_cap_gives_413_without_reading()
    {
        var http = Registry();
        bool called = false;
        http.Handle("discord", "me", PanelRole.Viewer, _ => { called = true; return PluginHttpReply.Ok("{}"); });

        var reply = PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "POST", Path, NoQuery,
            PluginHttpGate.MaxBody + 1, new ThrowingStream(), Limit);

        Assert.Equal(413, reply.Status);
        Assert.False(called);
    }

    [Fact]
    public void ContentLength_negative_one_with_an_oversized_stream_gives_413()
    {
        var http = Registry();
        http.Handle("discord", "me", PanelRole.Viewer, _ => PluginHttpReply.Ok("{}"));

        var body = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', PluginHttpGate.MaxBody + 1)));

        var reply = PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "POST", Path, NoQuery, -1, body, Limit);

        Assert.Equal(413, reply.Status);
    }

    [Fact]
    public void Multibyte_text_over_the_cap_in_bytes_but_under_it_in_chars_gives_413()
    {
        var http = Registry();
        bool called = false;
        http.Handle("discord", "me", PanelRole.Viewer, _ => { called = true; return PluginHttpReply.Ok("{}"); });

        // Three bytes each, so this is about 16 KB of bytes from about 5.5 K chars.
        string text = new string('€', PluginHttpGate.MaxBody / 3 + 1);
        Assert.True(text.Length <= PluginHttpGate.MaxBody);
        var body = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var reply = PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "POST", Path, NoQuery, -1, body, Limit);

        Assert.Equal(413, reply.Status);
        Assert.False(called);
    }

    [Fact]
    public void Multibyte_text_under_the_cap_in_bytes_arrives_decoded()
    {
        var http = Registry();
        string received = "";
        http.Handle("discord", "me", PanelRole.Viewer, req => { received = req.Body; return PluginHttpReply.Ok("{}"); });

        string text = new string('€', PluginHttpGate.MaxBody / 3);
        var body = new MemoryStream(Encoding.UTF8.GetBytes(text));

        Assert.Equal(200, PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "POST", Path, NoQuery, -1, body, Limit).Status);
        Assert.Equal(text, received);
    }

    [Fact]
    public void Exactly_the_cap_is_accepted()
    {
        var http = Registry();
        string received = "";
        http.Handle("discord", "me", PanelRole.Viewer, req => { received = req.Body; return PluginHttpReply.Ok("{}"); });

        string text = new string('a', PluginHttpGate.MaxBody);
        var body = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var reply = PluginHttpGate.Handle(http, PanelRole.Viewer, "acting", "POST", Path, NoQuery, text.Length, body, Limit);

        Assert.Equal(200, reply.Status);
        Assert.Equal(text, received);
    }
}
