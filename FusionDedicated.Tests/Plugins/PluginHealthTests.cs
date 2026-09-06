using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginHealthTests
{
    [Fact]
    public void A_plugin_starts_healthy()
    {
        Assert.False(new PluginHealth().IsDisabled("police"));
    }

    [Fact]
    public void Two_failures_are_not_enough_to_disable_it()
    {
        var health = new PluginHealth();

        Assert.False(health.NoteFailure("police"));
        Assert.False(health.NoteFailure("police"));
        Assert.False(health.IsDisabled("police"));
    }

    [Fact]
    public void The_third_failure_disables_it_and_says_so_once()
    {
        var health = new PluginHealth();

        health.NoteFailure("police");
        health.NoteFailure("police");

        Assert.True(health.NoteFailure("police"));
        Assert.True(health.IsDisabled("police"));

        // Already disabled, so this is not news.
        Assert.False(health.NoteFailure("police"));
    }

    [Fact]
    public void One_plugin_failing_does_not_touch_another()
    {
        var health = new PluginHealth();

        health.NoteFailure("police");
        health.NoteFailure("police");
        health.NoteFailure("police");

        Assert.False(health.IsDisabled("economy"));
    }

    [Fact]
    public void Forgetting_a_plugin_clears_its_record_for_a_reload()
    {
        var health = new PluginHealth();

        health.NoteFailure("police");
        health.NoteFailure("police");
        health.NoteFailure("police");

        health.Forget("police");

        Assert.False(health.IsDisabled("police"));
    }

    [Fact]
    public void A_verdict_carries_its_reason()
    {
        Assert.True(PluginVerdict.Allow.Allowed);
        Assert.False(PluginVerdict.Refuse("not police").Allowed);
        Assert.Equal("not police", PluginVerdict.Refuse("not police").Reason);
    }
}
