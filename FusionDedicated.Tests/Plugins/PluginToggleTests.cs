using FusionDedicated;

namespace FusionDedicated.Tests.Plugins;

public class PluginToggleTests
{
    [Fact]
    public void Plugins_are_off_unless_asked_for()
    {
        // A plugin is arbitrary code inside the server process, so an existing
        // server that knows nothing about plugins keeps behaving as it did.
        Assert.False(new ServerConfig().PluginsEnabled);
    }
}
