using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>
/// A service account is a bot's sign-in. It calls plugin routes marked for a
/// banker and reaches nothing else, so a leaked bot password cannot move money
/// through the panel's own banker blocks.
/// </summary>
public class ServiceRoleTests
{
    [Fact]
    public void A_service_account_reaches_a_plugin_route()
        => Assert.True(PanelPermissions.Allows(PanelRole.Service, "/api/plugins/http/discord/me"));

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/api/state")]
    [InlineData("/api/plugins")]
    [InlineData("/api/plugins/page")]
    [InlineData("/api/plugins/action")]
    [InlineData("/api/kick")]
    [InlineData("/api/accounts")]
    public void A_service_account_reaches_nothing_else(string route)
        => Assert.False(PanelPermissions.Allows(PanelRole.Service, route));

    [Fact]
    public void A_service_account_calls_banker_routes_only()
    {
        Assert.True(PanelPermissions.MayCall(PanelRole.Service, PanelRole.Banker));
        Assert.False(PanelPermissions.MayCall(PanelRole.Service, PanelRole.Viewer));
        Assert.False(PanelPermissions.MayCall(PanelRole.Service, PanelRole.Moderator));
        Assert.False(PanelPermissions.MayCall(PanelRole.Service, PanelRole.Owner));
    }

    [Fact]
    public void A_service_account_sees_no_page_blocks()
    {
        Assert.False(PanelPermissions.CanSee(PanelRole.Service, PanelRole.Viewer));
        Assert.False(PanelPermissions.CanSee(PanelRole.Service, PanelRole.Banker));
        Assert.False(PanelPermissions.CanSee(PanelRole.Service, PanelRole.Owner));
    }

    [Fact]
    public void The_word_service_in_the_accounts_file_is_read()
        => Assert.Equal(PanelRole.Service, PanelPermissions.ParseRole("service"));

    [Fact]
    public void The_gate_lets_a_service_account_reach_a_banker_route()
    {
        var http = new PluginHttp(new PluginHealth(), (_, _) => { });
        bool called = false;
        http.Handle("discord", "me", PanelRole.Banker, _ => { called = true; return PluginHttpReply.Ok("{}"); });

        var reply = PluginHttpGate.Handle(http, PanelRole.Service, "bot", "GET", "/api/plugins/http/discord/me",
            new Dictionary<string, string>(), 0, new MemoryStream());

        Assert.Equal(200, reply.Status);
        Assert.True(called);
    }
}
