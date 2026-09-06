using FusionDedicated.Web;

namespace FusionDedicated.Tests.Plugins;

public class PluginEndpointTests
{
    [Fact]
    public void The_plugin_list_is_open_to_anybody_who_can_see_the_panel()
    {
        Assert.True(PanelPermissions.Allows(PanelRole.Viewer, "/api/plugins"));
    }

    [Fact]
    public void A_plugin_page_needs_a_moderator()
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/plugins/page"));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/plugins/page"));
    }

    [Fact]
    public void A_plugin_action_needs_a_moderator()
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/plugins/action"));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/plugins/action"));
    }

    [Fact]
    public void An_endpoint_nobody_listed_is_still_refused()
    {
        // The default-deny that already protects the panel must not have been
        // loosened by adding a plugin prefix.
        Assert.False(PanelPermissions.Allows(PanelRole.Owner, "/api/plugins/secret"));
    }
}
