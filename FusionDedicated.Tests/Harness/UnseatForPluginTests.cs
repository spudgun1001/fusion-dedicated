using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Harness;

public class UnseatForPluginTests
{
    private const ushort Car = 400;
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    private static (World World, FakePlayer Joel, FakePlayer Kanza) CarDrivenByJoel()
    {
        var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");

        foreach (var player in world.Players)
        {
            player.FinishLoading();
            player.View.DriverLockedVehicles.Add(Car);
        }

        world.Spawn(joel, Car, "spudgun1001.BabasPolice.Spawnable.SedanPolice", 0, 0, 0);
        joel.Send(FusionProtocol.BuildSeat(joel.SmallId, Car, 0, true));

        return (world, joel, kanza);
    }

    [Fact]
    public void Unseating_a_player_who_is_not_seated_returns_false_and_sends_nothing()
    {
        var (world, joel, kanza) = CarDrivenByJoel();
        using var _ = world;
        int kanzaBefore = world.Transport.SentTo(kanza.Connection).Count;
        int joelBefore = world.Transport.SentTo(joel.Connection).Count;

        Assert.False(world.Server.UnseatForPlugin(KanzaId));
        Assert.False(world.Server.UnseatForPlugin(76561198000000099));

        Assert.Equal(kanzaBefore, world.Transport.SentTo(kanza.Connection).Count);
        Assert.Equal(joelBefore, world.Transport.SentTo(joel.Connection).Count);
    }

    [Fact]
    public void Unseating_a_driver_stands_them_up_for_everybody()
    {
        var (world, joel, kanza) = CarDrivenByJoel();
        using var _ = world;
        int joelBefore = world.Transport.SentTo(joel.Connection).Count;

        Assert.True(world.Server.UnseatForPlugin(JoelId));
        world.Sync();

        Assert.Empty(world.Server.RidersOf(Car));
        Assert.False(joel.View.Seats.ContainsKey(joel.SmallId));
        Assert.False(kanza.View.Seats.ContainsKey(joel.SmallId));
        Assert.Null(kanza.View.Entities[Car].LockedTo);

        var toJoel = world.Transport.SentTo(joel.Connection).Skip(joelBefore).Select(s => s.Message).ToList();
        Assert.Equal(FusionProtocol.BuildSeat(joel.SmallId, Car, 0, false), toJoel[0]);
        Assert.Equal(FusionProtocol.BuildOwnershipResponse(joel.SmallId, Car), toJoel[1]);

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"A plugin stood Joel up from seat 0 of entity {Car}");
    }

    [Fact]
    public void A_seat_is_found_by_platform_id()
    {
        var (world, _, _) = CarDrivenByJoel();
        using var __ = world;

        Assert.Equal(new PluginSeat(Car, 0), world.Server.SeatOfPlayer(JoelId));
        Assert.Null(world.Server.SeatOfPlayer(KanzaId));
        Assert.Null(world.Server.SeatOfPlayer(76561198000000099));
    }
}
