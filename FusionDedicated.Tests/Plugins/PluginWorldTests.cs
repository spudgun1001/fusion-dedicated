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

public class PluginWorldHandsAndSlotsTests
{
    [Fact]
    public void Holsters_and_hands_are_empty_without_a_server()
    {
        var world = new PluginWorld();

        Assert.Empty(world.Holstered(7));
        Assert.Empty(world.Held(7));
    }

    [Fact]
    public void Holsters_and_hands_come_from_the_server_lookups()
    {
        var world = new PluginWorld
        {
            HolsteredLookup = id => id == 7 ? new[] { new PluginSlot(2, 301) } : Array.Empty<PluginSlot>(),
            HeldLookup = id => id == 7 ? new ushort[] { 302 } : Array.Empty<ushort>(),
        };

        Assert.Equal(new[] { new PluginSlot(2, 301) }, world.Holstered(7));
        Assert.Equal(new ushort[] { 302 }, world.Held(7));
        Assert.Empty(world.Held(8));
    }
}
