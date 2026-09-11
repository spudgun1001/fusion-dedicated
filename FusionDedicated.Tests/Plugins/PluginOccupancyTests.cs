using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginOccupancyTests
{
    private static (float X, float Y, float Z) Origin => (0f, 0f, 0f);

    [Fact]
    public void A_holder_who_left_is_left_out()
    {
        var holders = PluginOccupancy.Holders(
            new byte[] { 1 },
            _ => null,
            _ => Origin,
            Origin);

        Assert.Empty(holders);
    }

    [Fact]
    public void A_holder_far_from_the_thing_is_not_holding_it()
    {
        var holders = PluginOccupancy.Holders(
            new byte[] { 1 },
            _ => 76561198000000001,
            _ => (10f, 0f, 0f),
            Origin);

        Assert.Empty(holders);
    }

    [Fact]
    public void An_unknown_position_keeps_the_holder()
    {
        var holders = PluginOccupancy.Holders(
            new byte[] { 1 },
            _ => 76561198000000001,
            _ => null,
            Origin);

        Assert.Equal(new ulong[] { 76561198000000001 }, holders);
    }

    [Fact]
    public void A_holder_within_reach_is_kept()
    {
        var holders = PluginOccupancy.Holders(
            new byte[] { 1 },
            _ => 76561198000000001,
            _ => (1f, 0f, 0f),
            Origin);

        Assert.Equal(new ulong[] { 76561198000000001 }, holders);
    }
}
