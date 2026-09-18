using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A SWAT van nine clients all believed they owned took 2,384 owner changes in one minute,
/// about 58 a second, and every one snapped it to a different player's copy. An entity that
/// has just changed hands stays put for a moment, and each player has an allowance of asks.
/// </summary>
public class OwnershipThrashTests
{
    private const ushort Van = 1820;

    private static World NewWorld(int hold = 500, int perSecond = 10)
        => new(new ServerConfig
        {
            CullOrphanedEntities = false,
            OwnershipHoldMilliseconds = hold,
            OwnershipRequestsPerSecond = perSecond,
        });

    private static (World World, FakePlayer Enzo, FakePlayer Jay, FakePlayer Kanza) VanOwnedByEnzo(
        int hold = 500, int perSecond = 10)
    {
        var world = NewWorld(hold, perSecond);
        var enzo = world.Join(76561198000000001, "Enzo");
        var jay = world.Join(76561198000000002, "JAY");
        var kanza = world.Join(76561198000000003, "Kanzaaa");

        foreach (var player in world.Players)
        {
            player.FinishLoading();
        }

        world.Spawn(enzo, Van, "spudgun1001.BabasPolice.Spawnable.VanSWATTransport", 0, 0, 0);

        return (world, enzo, jay, kanza);
    }

    private static void Ask(FakePlayer player)
        => player.Send(FusionProtocol.BuildOwnershipRequest(player.SmallId, Van));

    private static byte? Owner(World world) => world.Server.Entities.Get(Van)!.OwnerSmallId;

    private static int Changes(World world)
        => world.Server.RecentLog(4000).Count(e => e.Message.StartsWith($"Ownership of entity {Van} given"));

    private static IReadOnlyList<byte> OwnersToldTo(World world, FakePlayer player, int from)
        => world.Transport.SentTo(player.Connection)
            .Skip(from)
            .Select(sent => FusionProtocol.TryReadOwnershipResponse(sent.Message))
            .Where(response => response is { } read && read.EntityId == Van)
            .Select(response => response!.Value.PlayerId)
            .ToList();

    [Fact]
    public void A_second_change_inside_the_window_is_refused()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo();
        using var _2 = world;

        Ask(jay);
        world.Advance(TimeSpan.FromMilliseconds(499));
        Ask(kanza);

        Assert.Equal(jay.SmallId, Owner(world));
        Assert.True(world.AgreeOnOwner(Van), string.Join(", ", world.OwnersOf(Van)));
    }

    [Fact]
    public void A_change_after_the_window_is_allowed()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo();
        using var _2 = world;

        Ask(jay);
        world.Advance(TimeSpan.FromMilliseconds(500));
        Ask(kanza);

        Assert.Equal(kanza.SmallId, Owner(world));
        Assert.True(world.AgreeOnOwner(Van), string.Join(", ", world.OwnersOf(Van)));
    }

    [Fact]
    public void A_refused_asker_is_told_who_owns_it()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo();
        using var _2 = world;

        Ask(jay);
        int before = world.Transport.SentTo(kanza.Connection).Count;
        Ask(kanza);

        Assert.Equal(new[] { jay.SmallId }, OwnersToldTo(world, kanza, before));
    }

    [Fact]
    public void The_owner_asking_again_is_not_held_back()
    {
        var (world, _, jay, _2) = VanOwnedByEnzo();
        using var _3 = world;

        Ask(jay);
        int before = world.Transport.SentTo(jay.Connection).Count;
        Ask(jay);

        Assert.Equal(jay.SmallId, Owner(world));
        Assert.Equal(new[] { jay.SmallId }, OwnersToldTo(world, jay, before));
    }

    [Fact]
    public void A_rider_of_the_van_is_not_held_back()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo();
        using var _2 = world;

        Ask(jay);
        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Van, 2, true));
        Ask(kanza);

        Assert.Equal(kanza.SmallId, Owner(world));
    }

    [Fact]
    public void Somebody_holding_the_van_is_not_held_back()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo();
        using var _2 = world;

        Ask(jay);
        kanza.Send(FusionProtocol.BuildGrab(kanza.SmallId, FusionProtocol.Handedness.RIGHT, 0, Van));
        Ask(kanza);

        Assert.Equal(kanza.SmallId, Owner(world));
    }

    [Fact]
    public void A_hold_of_zero_lets_every_change_through()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo(hold: 0);
        using var _2 = world;

        Ask(jay);
        Ask(kanza);

        Assert.Equal(kanza.SmallId, Owner(world));
        Assert.Equal(2, Changes(world));
    }

    [Fact]
    public void Asks_over_the_allowance_are_dropped_and_come_back_the_next_second()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo(hold: 0, perSecond: 2);
        using var _2 = world;

        for (var bump = 0; bump < 5; bump++)
        {
            Ask(jay);
            Ask(kanza);
        }

        Assert.Equal(4, Changes(world));

        world.Advance(TimeSpan.FromSeconds(1));
        Ask(jay);

        Assert.Equal(5, Changes(world));
    }

    [Fact]
    public void An_allowance_of_zero_drops_nothing()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo(hold: 0, perSecond: 0);
        using var _2 = world;

        for (var bump = 0; bump < 10; bump++)
        {
            Ask(jay);
            Ask(kanza);
        }

        Assert.Equal(20, Changes(world));
    }

    [Fact]
    public void Two_players_fighting_over_the_van_for_two_seconds_change_it_a_handful_of_times()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo();
        using var _2 = world;

        for (var tick = 0; tick < 60; tick++)
        {
            Ask(tick % 2 == 0 ? jay : kanza);
            world.Advance(TimeSpan.FromMilliseconds(33));
        }

        Assert.InRange(Changes(world), 1, 6);
        Assert.True(world.AgreeOnOwner(Van), string.Join(", ", world.OwnersOf(Van)));
    }

    [Fact]
    public void Refusals_are_summed_up_rather_than_logged_one_by_one()
    {
        var (world, _, jay, kanza) = VanOwnedByEnzo();
        using var _2 = world;

        Ask(jay);

        for (var bump = 0; bump < 20; bump++)
        {
            Ask(kanza);
        }

        Assert.Equal(1, world.Server.RecentLog(4000)
            .Count(e => e.Message.StartsWith($"Refused Kanzaaa ownership of entity {Van}, which changed hands")));
    }
}
