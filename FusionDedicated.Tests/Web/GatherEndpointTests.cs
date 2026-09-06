using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class GatherEndpointTests
{
    [Fact]
    public void Bringing_everyone_to_one_player_needs_a_moderator()
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/gather"));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/gather"));
        Assert.True(PanelPermissions.Allows(PanelRole.Owner, "/api/gather"));
    }

    [Fact]
    public void Marking_a_prop_persistent_needs_a_moderator()
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/persist"));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/persist"));
    }
}
