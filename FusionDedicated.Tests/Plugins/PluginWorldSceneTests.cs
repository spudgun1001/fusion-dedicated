using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginWorldSceneTests
{
    [Fact]
    public void No_level_object_is_found_without_a_server()
    {
        Assert.Null(new PluginWorld().SceneEntity(1234, 0));
    }

    [Fact]
    public void A_level_object_comes_from_the_server_lookup()
    {
        var world = new PluginWorld
        {
            SceneEntityLookup = (hash, index) => hash == 1234 && index == 0 ? (ushort)700 : null,
        };

        Assert.Equal((ushort)700, world.SceneEntity(1234, 0));
        Assert.Null(world.SceneEntity(1234, 1));
    }
}
