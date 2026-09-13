using FusionDedicated.Plugins;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

/// <summary>What of a plugin page one panel account is built, shown and allowed to press.</summary>
public class PluginPageAccessTests
{
    private static PluginPanel Panel() => new(new PluginHealth(), (_, _) => { });

    private static PluginPage LabRp() => new PluginPage("LabRP")
        .Fields("Balances", Array.Empty<PluginField>(), new[] { new PluginButton("Give", "give") })
        .Fields("Everyone else", Array.Empty<PluginField>(), new[] { new PluginButton("Save", "settings") })
        .OnlyFor(PanelRole.Banker);

    [Fact]
    public void The_page_is_built_for_the_account_asking()
    {
        var panel = Panel();
        PluginViewer? seen = null;

        panel.Register("labrp", viewer =>
        {
            seen = viewer;
            return LabRp();
        });

        PluginPageAccess.Visible(panel, "labrp", new PluginViewer("Sam", PanelRole.Moderator));

        Assert.Equal(new PluginViewer("Sam", PanelRole.Moderator), seen);
    }

    [Fact]
    public void A_banker_sees_only_the_blocks_set_aside_for_them()
    {
        var panel = Panel();
        panel.Register("labrp", _ => LabRp());

        var page = PluginPageAccess.Visible(panel, "labrp", new PluginViewer("Bank", PanelRole.Banker));

        Assert.Equal(new[] { "Everyone else" }, page!.Sections.Select(s => s.Title));
    }

    [Fact]
    public void An_account_below_the_page_floor_gets_no_page()
    {
        var panel = Panel();
        panel.Register("labrp", _ => LabRp());

        Assert.Null(PluginPageAccess.Visible(panel, "labrp", new PluginViewer("Val", PanelRole.Viewer)));
    }

    [Fact]
    public void An_account_may_press_only_the_buttons_it_is_shown()
    {
        var panel = Panel();
        panel.Register("labrp", _ => LabRp());
        var banker = new PluginViewer("Bank", PanelRole.Banker);

        Assert.True(PluginPageAccess.MayInvoke(panel, "labrp", "settings", banker));
        Assert.False(PluginPageAccess.MayInvoke(panel, "labrp", "give", banker));
        Assert.True(PluginPageAccess.MayInvoke(panel, "labrp", "give", new PluginViewer("Sam", PanelRole.Moderator)));
    }

    [Fact]
    public void A_row_button_may_be_pressed_only_by_an_account_shown_its_table()
    {
        var panel = Panel();
        panel.Register("labrp", _ => new PluginPage("LabRP")
            .Table("Balances", new[] { "Player" },
                new[] { new PluginRow(new[] { "Joel" }).With(new PluginButton("Give 100", "give")) })
            .Fields("Everyone else", Array.Empty<PluginField>(), new[] { new PluginButton("Save", "settings") })
            .OnlyFor(PanelRole.Banker));

        Assert.True(PluginPageAccess.MayInvoke(panel, "labrp", "give", new PluginViewer("Sam", PanelRole.Moderator)));
        Assert.False(PluginPageAccess.MayInvoke(panel, "labrp", "give", new PluginViewer("Bank", PanelRole.Banker)));
    }

    [Fact]
    public void No_panel_means_no_page()
        => Assert.Null(PluginPageAccess.Visible(null, "labrp", new PluginViewer("Sam", PanelRole.Owner)));
}
