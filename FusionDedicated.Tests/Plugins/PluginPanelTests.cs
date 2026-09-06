using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginPanelTests
{
    private readonly List<string> _log = new();
    private readonly PluginHealth _health = new();

    private PluginPanel Panel() => new(_health, (l, m) => _log.Add(l + " " + m));

    [Fact]
    public void A_plugin_with_no_page_offers_none()
    {
        Assert.Empty(Panel().Pages);
        Assert.Null(Panel().Build("police"));
    }

    [Fact]
    public void A_registered_page_is_listed_and_built()
    {
        var panel = Panel();
        panel.Register("police", () => new PluginPage("Police"));

        Assert.Equal(new[] { "police" }, panel.Pages);
        Assert.Equal("Police", panel.Build("police")!.Title);
    }

    [Fact]
    public void The_page_is_rebuilt_each_time_so_it_shows_live_data()
    {
        var panel = Panel();
        var count = 0;

        panel.Register("police", () => new PluginPage($"Police {++count}"));

        Assert.Equal("Police 1", panel.Build("police")!.Title);
        Assert.Equal("Police 2", panel.Build("police")!.Title);
    }

    [Fact]
    public void A_builder_that_throws_gives_no_page_rather_than_taking_the_panel_down()
    {
        var panel = Panel();
        panel.Register("broken", () => throw new InvalidOperationException("boom"));

        Assert.Null(panel.Build("broken"));
        Assert.Contains(_log, line => line.Contains("broken"));
    }

    [Fact]
    public void An_action_reaches_its_handler_with_the_values()
    {
        var panel = Panel();
        var seen = "";

        panel.OnAction("police", "add", values => seen = values["steamId"]);

        var result = panel.Invoke("police", "add",
            new Dictionary<string, string> { ["steamId"] = "7656119800000001" });

        Assert.True(result.Handled);
        Assert.Equal("7656119800000001", seen);
    }

    [Fact]
    public void An_action_nobody_registered_is_reported_rather_than_ignored()
    {
        var result = Panel().Invoke("police", "nothing", new Dictionary<string, string>());

        Assert.False(result.Handled);
        Assert.NotEqual("", result.Error);
    }

    [Fact]
    public void An_action_that_throws_is_reported_and_counted()
    {
        var panel = Panel();
        panel.OnAction("broken", "go", _ => throw new InvalidOperationException("boom"));

        var result = panel.Invoke("broken", "go", new Dictionary<string, string>());

        Assert.False(result.Handled);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public void A_disabled_plugin_is_not_asked_for_a_page_or_an_action()
    {
        var panel = Panel();
        var built = false;

        panel.Register("broken", () => { built = true; return new PluginPage("x"); });
        panel.OnAction("broken", "go", _ => { });

        _health.NoteFailure("broken");
        _health.NoteFailure("broken");
        _health.NoteFailure("broken");

        Assert.Null(panel.Build("broken"));
        Assert.False(built);
        Assert.False(panel.Invoke("broken", "go", new Dictionary<string, string>()).Handled);
    }

    [Fact]
    public void Removing_a_plugin_takes_its_page_and_actions_with_it()
    {
        var panel = Panel();
        panel.Register("police", () => new PluginPage("Police"));
        panel.OnAction("police", "add", _ => { });

        panel.RemoveAll("police");

        Assert.Empty(panel.Pages);
        Assert.False(panel.Invoke("police", "add", new Dictionary<string, string>()).Handled);
    }
}
