using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>A plugin can say which panel account pressed a button, for an audit log.</summary>
public class PanelUserTests
{
    [Fact]
    public void The_panel_account_that_pressed_the_button_is_read()
        => Assert.Equal("Sam", PluginValues.PanelUser(
            new Dictionary<string, string> { [PluginValues.PanelUserKey] = "Sam" }));

    [Fact]
    public void An_older_server_that_sends_no_account_gives_an_empty_name()
        => Assert.Equal("", PluginValues.PanelUser(new Dictionary<string, string>()));
}
