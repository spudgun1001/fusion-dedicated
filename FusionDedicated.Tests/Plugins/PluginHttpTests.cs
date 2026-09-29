using FusionDedicated;
using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Plugins;

public class PluginHttpTests
{
    private readonly List<string> _log = new();
    private readonly PluginHealth _health = new();

    private PluginHttp Http() => new(_health, (l, m) => _log.Add(l + " " + m));

    private static PluginHttpRequest Request(string path) => new("POST", path, new Dictionary<string, string>(), "{}", "bot");

    [Fact]
    public void A_registered_route_answers_and_says_its_role()
    {
        var http = Http();
        http.Handle("discord", "me", PanelRole.Banker, r => PluginHttpReply.Ok("{\"path\":\"" + r.Path + "\"}"));

        Assert.Equal(PanelRole.Banker, http.RoleFor("discord", "me"));
        var reply = http.Invoke("discord", "me", Request("me"));
        Assert.Equal(200, reply.Status);
        Assert.Equal("{\"path\":\"me\"}", reply.Json);
    }

    [Fact]
    public void An_unknown_route_has_no_role()
    {
        Assert.Null(Http().RoleFor("discord", "nope"));
        Assert.Equal(404, Http().Invoke("discord", "nope", Request("nope")).Status);
    }

    [Fact]
    public void A_handler_that_throws_answers_500_and_counts_against_the_plugin()
    {
        var http = Http();
        http.Handle("discord", "boom", PanelRole.Banker, _ => throw new InvalidOperationException("bad"));

        Assert.Equal(500, http.Invoke("discord", "boom", Request("boom")).Status);
        Assert.Contains(_log, l => l.Contains("discord"));
    }

    [Fact]
    public void A_disabled_plugin_has_no_routes()
    {
        var http = Http();
        http.Handle("discord", "me", PanelRole.Banker, _ => PluginHttpReply.Ok("{}"));
        for (int i = 0; i < PluginHealth.FailuresBeforeDisable; i++) _health.NoteFailure("discord");

        Assert.Null(http.RoleFor("discord", "me"));
    }

    [Fact]
    public void RemoveAll_takes_only_that_plugins_routes()
    {
        var http = Http();
        http.Handle("discord", "me", PanelRole.Banker, _ => PluginHttpReply.Ok("{}"));
        http.Handle("other", "me", PanelRole.Owner, _ => PluginHttpReply.Ok("{}"));

        http.RemoveAll("discord");

        Assert.Null(http.RoleFor("discord", "me"));
        Assert.Equal(PanelRole.Owner, http.RoleFor("other", "me"));
    }

    [Fact]
    public void An_error_reply_escapes_its_message()
        => Assert.Equal("{\"error\":\"say \\u0022hi\\u0022\"}", PluginHttpReply.Error(400, "say \"hi\"").Json);

    [Fact]
    public void A_context_has_routes_even_when_the_host_gave_none()
    {
        var context = new PluginContext("x", new PluginEvents(_health, (_, _) => { }), new PluginStore(Path.GetTempFileName()),
            new PluginPanel(_health, (_, _) => { }), new PluginModules(_health, (_, _) => { }), new NoActions(), () => Array.Empty<PluginPlayer>(), (_, _) => { });
        Assert.NotNull(context.Http);
    }

    private sealed class NoActions : IPluginActions
    {
        public void Kick(ulong platformId, string reason) { }
        public void Ban(ulong platformId, string reason) { }
        public void SetRank(ulong platformId, PermissionLevel level) { }
        public void Despawn(ushort entityId) { }
        public void SendModule(ulong platformId, long tag, byte[] payload) { }
        public void BroadcastModule(long tag, byte[] payload) { }
    }
}
