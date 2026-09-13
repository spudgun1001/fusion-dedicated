using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Plugins;

/// <summary>A plugin page built for the panel account looking at it.</summary>
public class PluginViewerTests
{
    private static PluginPanel Panel() => new(new PluginHealth(), (_, _) => { });

    [Fact]
    public void A_page_built_for_a_viewer_is_given_their_name_and_role()
    {
        var panel = Panel();
        PluginViewer? seen = null;

        panel.Register("labrp", viewer =>
        {
            seen = viewer;
            return new PluginPage("LabRP");
        });

        Assert.NotNull(panel.Build("labrp", new PluginViewer("Sam", PanelRole.Moderator)));
        Assert.Equal(new PluginViewer("Sam", PanelRole.Moderator), seen);
    }

    [Fact]
    public void A_page_registered_without_a_viewer_still_builds_both_ways()
    {
        var panel = Panel();
        panel.Register("police", () => new PluginPage("Police"));

        Assert.Equal("Police", panel.Build("police")!.Title);
        Assert.Equal("Police", panel.Build("police", new PluginViewer("Sam", PanelRole.Viewer))!.Title);
    }

    [Fact]
    public void Building_without_a_viewer_builds_for_the_panel_owner()
    {
        var panel = Panel();
        PluginViewer? seen = null;

        panel.Register("labrp", viewer =>
        {
            seen = viewer;
            return new PluginPage("LabRP");
        });

        panel.Build("labrp");

        Assert.Equal(new PluginViewer("panel", PanelRole.Owner), seen);
    }

    [Fact]
    public void A_viewer_builder_that_throws_gives_no_page()
    {
        var panel = Panel();
        panel.Register("broken", _ => throw new InvalidOperationException("boom"));

        Assert.Null(panel.Build("broken", new PluginViewer("Sam", PanelRole.Owner)));
    }
}
