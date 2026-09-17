using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginWorldSeatTests
{
    [Fact]
    public void Nobody_is_seated_without_a_server()
    {
        Assert.Null(new PluginWorld().SeatOf(76561198000000001));
    }

    [Fact]
    public void A_seat_comes_from_the_server_lookup()
    {
        var world = new PluginWorld
        {
            SeatOfLookup = id => id == 76561198000000001 ? new PluginSeat(400, 0) : null,
        };

        Assert.Equal(new PluginSeat(400, 0), world.SeatOf(76561198000000001));
        Assert.Null(world.SeatOf(76561198000000002));
    }
}
