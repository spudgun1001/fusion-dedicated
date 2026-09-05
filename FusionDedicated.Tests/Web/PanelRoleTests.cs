using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>
/// One table decides what a role may reach, so a handler cannot quietly go
/// unguarded. An endpoint nobody listed is refused rather than allowed.
/// </summary>
public class PanelRoleTests
{
    [Theory]
    [InlineData("/api/state")]
    [InlineData("/api/history")]
    public void Everyone_may_look(string path)
    {
        Assert.True(PanelPermissions.Allows(PanelRole.Viewer, path));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, path));
        Assert.True(PanelPermissions.Allows(PanelRole.Owner, path));
    }

    [Theory]
    [InlineData("/api/kick")]
    [InlineData("/api/ban")]
    [InlineData("/api/unban")]
    [InlineData("/api/mute")]
    [InlineData("/api/purge")]
    [InlineData("/api/permission")]
    [InlineData("/api/level")]
    [InlineData("/api/levels")]
    [InlineData("/api/clear")]
    public void A_viewer_may_not_act(string path)
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, path));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, path));
        Assert.True(PanelPermissions.Allows(PanelRole.Owner, path));
    }

    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/api/restart")]
    [InlineData("/api/accounts")]
    public void Only_an_owner_may_change_the_server_or_its_accounts(string path)
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, path));
        Assert.False(PanelPermissions.Allows(PanelRole.Moderator, path));
        Assert.True(PanelPermissions.Allows(PanelRole.Owner, path));
    }

    [Fact]
    public void An_endpoint_nobody_listed_is_refused_even_for_an_owner()
    {
        // Adding a handler and forgetting to place it must fail shut.
        Assert.False(PanelPermissions.Allows(PanelRole.Owner, "/api/something-new"));
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/something-new"));
    }

    [Fact]
    public void A_query_string_does_not_change_the_answer()
    {
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/kick?id=3"));
        Assert.False(PanelPermissions.Allows(PanelRole.Moderator, "/api/settings?maxPlayers=8"));
    }

    [Fact]
    public void The_page_itself_is_open_to_anyone_signed_in()
    {
        Assert.True(PanelPermissions.Allows(PanelRole.Viewer, "/"));
    }

    [Fact]
    public void Roles_are_read_from_the_file_forgivingly_and_default_to_the_least()
    {
        Assert.Equal(PanelRole.Owner, PanelPermissions.ParseRole("owner"));
        Assert.Equal(PanelRole.Owner, PanelPermissions.ParseRole("OWNER"));
        Assert.Equal(PanelRole.Moderator, PanelPermissions.ParseRole("Moderator"));
        Assert.Equal(PanelRole.Viewer, PanelPermissions.ParseRole("viewer"));
        Assert.Equal(PanelRole.Viewer, PanelPermissions.ParseRole("nonsense"));
        Assert.Equal(PanelRole.Viewer, PanelPermissions.ParseRole(null));
    }
}
