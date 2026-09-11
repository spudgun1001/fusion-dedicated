using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginWorldHoldersTests
{
    [Fact]
    public void Holders_are_nobody_without_a_server()
    {
        Assert.Empty(new PluginWorld().Holders(300));
    }

    [Fact]
    public void Holders_come_from_the_server_lookup()
    {
        var world = new PluginWorld
        {
            HoldersLookup = id => id == 300
                ? new ulong[] { 76561198000000001 }
                : Array.Empty<ulong>(),
        };

        Assert.Equal(new ulong[] { 76561198000000001 }, world.Holders(300));
        Assert.Empty(world.Holders(301));
    }
}
