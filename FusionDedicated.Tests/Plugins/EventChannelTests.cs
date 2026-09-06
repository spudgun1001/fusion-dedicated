using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class EventChannelTests
{
    private readonly List<string> _log = new();

    private EventChannel<string> Channel(PluginHealth? health = null)
        => new(health ?? new PluginHealth(), (level, message) => _log.Add(level + " " + message));

    [Fact]
    public void With_nobody_listening_everything_is_allowed()
    {
        Assert.True(Channel().Raise("anything").Allowed);
    }

    [Fact]
    public void A_handler_that_allows_lets_it_through()
    {
        var channel = Channel();
        channel.Subscribe("police", _ => PluginVerdict.Allow);

        Assert.True(channel.Raise("anything").Allowed);
    }

    [Fact]
    public void A_handler_that_refuses_stops_it_and_keeps_the_reason()
    {
        var channel = Channel();
        channel.Subscribe("police", _ => PluginVerdict.Refuse("not police"));

        var verdict = channel.Raise("anything");

        Assert.False(verdict.Allowed);
        Assert.Equal("not police", verdict.Reason);
    }

    [Fact]
    public void The_first_refusal_wins_and_later_handlers_do_not_run()
    {
        var channel = Channel();
        var reached = false;

        channel.Subscribe("first", _ => PluginVerdict.Refuse("no"));
        channel.Subscribe("second", _ => { reached = true; return PluginVerdict.Allow; });

        channel.Raise("anything");

        Assert.False(reached);
    }

    [Fact]
    public void Handlers_run_in_the_order_they_were_added()
    {
        var channel = Channel();
        var order = new List<string>();

        channel.Subscribe("a", _ => { order.Add("a"); return PluginVerdict.Allow; });
        channel.Subscribe("b", _ => { order.Add("b"); return PluginVerdict.Allow; });

        channel.Raise("anything");

        Assert.Equal(new[] { "a", "b" }, order);
    }

    [Fact]
    public void A_handler_that_throws_allows_the_event_rather_than_refusing_it()
    {
        var channel = Channel();
        channel.Subscribe("broken", _ => throw new InvalidOperationException("boom"));

        // Failing closed would look exactly like the server refusing everything,
        // which is far harder to diagnose than a rule quietly not applying.
        Assert.True(channel.Raise("anything").Allowed);
    }

    [Fact]
    public void A_handler_that_throws_does_not_stop_the_next_one()
    {
        var channel = Channel();
        var reached = false;

        channel.Subscribe("broken", _ => throw new InvalidOperationException("boom"));
        channel.Subscribe("fine", _ => { reached = true; return PluginVerdict.Allow; });

        channel.Raise("anything");

        Assert.True(reached);
    }

    [Fact]
    public void A_plugin_that_keeps_throwing_stops_being_called()
    {
        var health = new PluginHealth();
        var channel = Channel(health);
        var calls = 0;

        channel.Subscribe("broken", _ => { calls++; throw new InvalidOperationException("boom"); });

        for (var i = 0; i < 5; i++)
        {
            channel.Raise("anything");
        }

        Assert.Equal(PluginHealth.FailuresBeforeDisable, calls);
        Assert.True(health.IsDisabled("broken"));
    }

    [Fact]
    public void Removing_a_plugin_detaches_its_handlers()
    {
        var channel = Channel();
        channel.Subscribe("police", _ => PluginVerdict.Refuse("no"));

        channel.RemoveAll("police");

        Assert.True(channel.Raise("anything").Allowed);
        Assert.Equal(0, channel.Count);
    }

    [Fact]
    public void Removing_one_plugin_leaves_another_alone()
    {
        var channel = Channel();
        channel.Subscribe("a", _ => PluginVerdict.Allow);
        channel.Subscribe("b", _ => PluginVerdict.Refuse("no"));

        channel.RemoveAll("a");

        Assert.False(channel.Raise("anything").Allowed);
    }
}
