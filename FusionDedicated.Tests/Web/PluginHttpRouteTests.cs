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
    public void The_panel_checks_the_route_role_and_caps_the_body()
    {
        string source = DashboardSource.Text();
        Assert.Contains("\"/api/plugins/http/\"", source);
        Assert.Contains("PanelPermissions.MayCall(_actingRole", source);
        Assert.Contains("MaxPluginBody", source);
    }
}
