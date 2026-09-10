using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginWorldMotionTests
{
    [Fact]
    public void Motion_is_nothing_without_a_server()
    {
        Assert.Null(new PluginWorld().Motion(300));
    }

    [Fact]
    public void Motion_comes_from_the_server_lookup()
    {
        var world = new PluginWorld
        {
            MotionLookup = id => id == 300
                ? new PluginMotion(new byte[7], 3f, 4f, 0f, DateTime.UtcNow)
                : null,
        };

        Assert.Equal(5f, world.Motion(300)!.Value.Speed, 3);
        Assert.Null(world.Motion(301));
    }
}
