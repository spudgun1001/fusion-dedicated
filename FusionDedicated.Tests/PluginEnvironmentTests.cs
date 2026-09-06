namespace FusionDedicated.Tests;

public class PluginEnvironmentTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" true ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    public void The_egg_switch_turns_plugins_on(string value)
    {
        var config = new ServerConfig();

        Assert.True(config.ApplyPluginEnvironment(value));
        Assert.True(config.PluginsEnabled);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("anything else")]
    public void The_egg_switch_turns_plugins_off(string value)
    {
        var config = new ServerConfig { PluginsEnabled = true };

        Assert.False(config.ApplyPluginEnvironment(value));
        Assert.False(config.PluginsEnabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_variable_leaves_server_json_alone(string? value)
    {
        // Running outside the egg must not turn off a server that was set up by hand.
        var config = new ServerConfig { PluginsEnabled = true };

        Assert.Null(config.ApplyPluginEnvironment(value));
        Assert.True(config.PluginsEnabled);
    }
}
