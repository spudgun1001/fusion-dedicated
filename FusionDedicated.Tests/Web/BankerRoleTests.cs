using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>
/// A banker opens the panel to move money between two accounts and to do nothing
/// else. They are not a rung on the ladder the other three roles sit on, so every
/// check has to name them rather than compare them, and the risk in getting it
/// wrong is that Banker holds the highest number and would otherwise outrank an
/// owner everywhere.
/// </summary>
public class BankerRoleTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/api/state")]
    [InlineData("/api/plugins")]
    [InlineData("/api/plugins/page")]
    [InlineData("/api/plugins/action")]
    public void A_banker_reaches_the_panel_and_its_plugin_pages(string route)
        => Assert.True(PanelPermissions.Allows(PanelRole.Banker, route));

    [Theory]
    [InlineData("/api/kick")]
    [InlineData("/api/ban")]
    [InlineData("/api/unban")]
    [InlineData("/api/mute")]
    [InlineData("/api/purge")]
    [InlineData("/api/permission")]
    [InlineData("/api/level")]
    [InlineData("/api/clear")]
    [InlineData("/api/despawn")]
    [InlineData("/api/settings")]
    [InlineData("/api/restart")]
    [InlineData("/api/accounts")]
    [InlineData("/api/history")]
    [InlineData("/api/audit")]
    [InlineData("/api/modules")]
    public void A_banker_reaches_nothing_else(string route)
        => Assert.False(PanelPermissions.Allows(PanelRole.Banker, route));

    [Fact]
    public void Holding_the_highest_number_does_not_make_a_banker_an_owner()
    {
        // The whole reason the check names the role instead of comparing it.
        Assert.True((int)PanelRole.Banker > (int)PanelRole.Owner);
        Assert.False(PanelPermissions.Allows(PanelRole.Banker, "/api/accounts"));
    }

    [Fact]
    public void The_other_three_are_unchanged()
    {
        Assert.True(PanelPermissions.Allows(PanelRole.Owner, "/api/accounts"));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/kick"));
        Assert.False(PanelPermissions.Allows(PanelRole.Moderator, "/api/settings"));
        Assert.True(PanelPermissions.Allows(PanelRole.Viewer, "/api/state"));
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/kick"));
    }

    [Fact]
    public void An_unlisted_route_is_still_refused_to_everybody()
        => Assert.False(PanelPermissions.Allows(PanelRole.Owner, "/api/whatever"));

    [Fact]
    public void The_word_banker_in_the_accounts_file_is_read()
    {
        Assert.Equal(PanelRole.Banker, PanelPermissions.ParseRole("banker"));
        Assert.Equal(PanelRole.Banker, PanelPermissions.ParseRole(" Banker "));
    }

    [Fact]
    public void Anything_unrecognised_is_still_the_least_privilege()
        => Assert.Equal(PanelRole.Viewer, PanelPermissions.ParseRole("bankerish"));

    [Fact]
    public void A_banker_sees_only_the_blocks_put_aside_for_them()
    {
        Assert.True(PanelPermissions.CanSee(PanelRole.Banker, PanelRole.Banker));
        Assert.False(PanelPermissions.CanSee(PanelRole.Banker, PanelRole.Moderator));
        Assert.False(PanelPermissions.CanSee(PanelRole.Banker, PanelRole.Viewer));
        Assert.False(PanelPermissions.CanSee(PanelRole.Banker, PanelRole.Owner));
    }

    [Fact]
    public void Everybody_else_still_sees_the_whole_page()
    {
        // A block marked for bankers is ordinary moderation to a moderator, so
        // setting one aside must not take it away from the people running the
        // server.
        Assert.True(PanelPermissions.CanSee(PanelRole.Moderator, PanelRole.Banker));
        Assert.True(PanelPermissions.CanSee(PanelRole.Owner, PanelRole.Banker));
        Assert.False(PanelPermissions.CanSee(PanelRole.Viewer, PanelRole.Banker));
    }

    [Fact]
    public void The_ordinary_ladder_is_untouched()
    {
        Assert.True(PanelPermissions.CanSee(PanelRole.Owner, PanelRole.Moderator));
        Assert.True(PanelPermissions.CanSee(PanelRole.Moderator, PanelRole.Moderator));
        Assert.False(PanelPermissions.CanSee(PanelRole.Viewer, PanelRole.Moderator));
    }

    [Fact]
    public void A_block_is_marked_by_the_call_after_it_and_nothing_else()
    {
        var page = new PluginPage("LabRP")
            .Table("Balances", new[] { "Player" }, Array.Empty<PluginRow>())
            .Fields("Move money", Array.Empty<PluginField>(),
                new[] { new PluginButton("Send", "transfer") })
            .OnlyFor(PanelRole.Banker)
            .Fields("Everyone else", Array.Empty<PluginField>(),
                new[] { new PluginButton("Save", "settings") });

        Assert.Equal(PanelRole.Moderator, page.Sections[0].Required);
        Assert.Equal(PanelRole.Banker, page.Sections[1].Required);
        Assert.Equal(PanelRole.Moderator, page.Sections[2].Required);
    }

    [Fact]
    public void Marking_a_page_with_no_blocks_yet_does_nothing_rather_than_throwing()
        => new PluginPage("Empty").OnlyFor(PanelRole.Banker);
}
