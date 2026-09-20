using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// The gang plugin refused the driver seat of one police SUV 365 times, 342 of them in four
/// minutes, because Fusion re-registers the seat while the rider stands in its trigger. Each
/// refusal sent them an egress their game applied at once, so the rig went in and out of the
/// seat about three times a second. A refused rider is stood up once and then left alone.
/// </summary>
public class SeatRefusalLoopTests
{
    private const ushort Suv = 893;
    private const ushort Van = 1820;
    private const string PoliceSuv = "spudgun1001.BabasPolice.Spawnable.SUVPolice";
    private const ulong JoelId = 76561198000000001;
    private const ulong SiriussId = 76561198000000002;
    private const ulong DennisId = 76561198000000003;

    private static (World World, FakePlayer Joel, FakePlayer Siriuss, FakePlayer Dennis) PoliceSuvOwnedByJoel(
        double cooldown = 2)
    {
        var world = new World(new ServerConfig
        {
            CullOrphanedEntities = false,
            SeatRefusalCooldownSeconds = cooldown,
        });

        var joel = world.Join(JoelId, "Joel");
        var siriuss = world.Join(SiriussId, "siriuss");
        var dennis = world.Join(DennisId, "Dennis");

        foreach (var player in world.Players)
        {
            player.FinishLoading();
            player.View.DriverLockedVehicles.Add(Suv);
        }

        // Long enough for every rig to be built and asked about, so nothing here counts those.
        world.Advance(TimeSpan.FromSeconds(5));
        world.Spawn(joel, Suv, PoliceSuv, 0, 0, 0);

        return (world, joel, siriuss, dennis);
    }

    /// <summary>The gang rule: seat 0 of a police vehicle is Joel's, and the plugin counts what it is asked.</summary>
    private static List<SeatEvent> OnlyJoelDrives(World world)
    {
        var seen = new List<SeatEvent>();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });

        events.Seat.Subscribe("gangs", e =>
        {
            seen.Add(e);

            return e.Ingress && e.SeatIndex == 0 && e.PlatformId != JoelId
                ? PluginVerdict.Refuse("that driver seat is for listed gang roles only")
                : PluginVerdict.Allow;
        });

        world.Server.Plugins = events;

        return seen;
    }

    private static void Sit(FakePlayer player, ushort entity, byte index)
        => player.Send(FusionProtocol.BuildSeat(player.SmallId, entity, index, true));

    /// <summary>The egresses stamped as this player that the server has sent them since a mark.</summary>
    private static int StandUps(World world, FakePlayer player, int from, ushort entity, byte index)
        => world.Transport.SentTo(player.Connection)
            .Skip(from)
            .Count(sent => sent.Message.SequenceEqual(
                FusionProtocol.BuildSeat(player.SmallId, entity, index, false)));

    [Fact]
    public void A_second_ingress_inside_the_window_sends_nothing_and_asks_no_plugin()
    {
        var (world, _, siriuss, dennis) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);
        int mine = world.Transport.SentTo(siriuss.Connection).Count;
        int theirs = world.Transport.SentTo(dennis.Connection).Count;

        Sit(siriuss, Suv, 0);
        int afterFirst = world.Transport.SentTo(siriuss.Connection).Count;

        world.Advance(TimeSpan.FromMilliseconds(300));
        Sit(siriuss, Suv, 0);

        Assert.Single(seen);
        Assert.Equal(1, StandUps(world, siriuss, mine, Suv, 0));
        Assert.Equal(afterFirst, world.Transport.SentTo(siriuss.Connection).Count);
        Assert.Equal(theirs, world.Transport.SentTo(dennis.Connection).Count);
        Assert.Empty(world.Server.RidersOf(Suv));
    }

    [Fact]
    public void After_the_window_the_rider_is_refused_again()
    {
        var (world, _, siriuss, _) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);
        int mine = world.Transport.SentTo(siriuss.Connection).Count;

        Sit(siriuss, Suv, 0);
        world.Advance(TimeSpan.FromSeconds(2));
        Sit(siriuss, Suv, 0);

        Assert.Equal(2, seen.Count);
        Assert.Equal(2, StandUps(world, siriuss, mine, Suv, 0));
        Assert.Empty(world.Server.RidersOf(Suv));
    }

    [Fact]
    public void Another_seat_of_the_same_vehicle_is_not_suppressed()
    {
        var (world, _, siriuss, dennis) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);

        Sit(siriuss, Suv, 0);
        Sit(siriuss, Suv, 1);

        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { siriuss.SmallId }, world.Server.RidersOf(Suv));
        Assert.Equal((Suv, (byte)1), dennis.View.Seats[siriuss.SmallId]);
    }

    [Fact]
    public void Another_vehicle_is_not_suppressed()
    {
        var (world, joel, siriuss, _) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        world.Spawn(joel, Van, "spudgun1001.BabasPolice.Spawnable.VanSWATTransport", 0, 0, 0);
        var seen = OnlyJoelDrives(world);
        int mine = world.Transport.SentTo(siriuss.Connection).Count;

        Sit(siriuss, Suv, 0);
        Sit(siriuss, Van, 0);

        Assert.Equal(2, seen.Count);
        Assert.Equal(1, StandUps(world, siriuss, mine, Suv, 0));
        Assert.Equal(1, StandUps(world, siriuss, mine, Van, 0));
    }

    [Fact]
    public void Another_player_is_not_suppressed()
    {
        var (world, _, siriuss, dennis) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);
        int theirs = world.Transport.SentTo(dennis.Connection).Count;

        Sit(siriuss, Suv, 0);
        Sit(dennis, Suv, 0);

        Assert.Equal(2, seen.Count);
        Assert.Equal(1, StandUps(world, dennis, theirs, Suv, 0));
    }

    [Fact]
    public void An_allowed_ingress_is_never_suppressed()
    {
        var (world, joel, _, dennis) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);

        Sit(joel, Suv, 0);
        Sit(joel, Suv, 0);

        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { joel.SmallId }, world.Server.RidersOf(Suv));
        Assert.Equal((Suv, (byte)0), dennis.View.Seats[joel.SmallId]);
    }

    [Fact]
    public void A_cooldown_of_zero_suppresses_nothing()
    {
        var (world, _, siriuss, _) = PoliceSuvOwnedByJoel(cooldown: 0);
        using var _2 = world;
        var seen = OnlyJoelDrives(world);
        int mine = world.Transport.SentTo(siriuss.Connection).Count;

        Sit(siriuss, Suv, 0);
        Sit(siriuss, Suv, 0);

        Assert.Equal(2, seen.Count);
        Assert.Equal(2, StandUps(world, siriuss, mine, Suv, 0));
    }

    [Fact]
    public void A_rider_who_leaves_is_forgotten()
    {
        var (world, _, siriuss, _) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);

        Sit(siriuss, Suv, 0);
        world.Leave(siriuss, "Closing Connection");

        var again = world.Join(SiriussId, "siriuss");
        again.FinishLoading();
        Assert.Equal(siriuss.SmallId, again.SmallId);

        int mine = world.Transport.SentTo(again.Connection).Count;
        Sit(again, Suv, 0);

        Assert.Equal(2, seen.Count);
        Assert.Equal(1, StandUps(world, again, mine, Suv, 0));
    }

    [Fact]
    public void A_level_change_forgets_the_refusals()
    {
        var (world, joel, siriuss, _) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);

        Sit(siriuss, Suv, 0);

        world.Server.SetLevel("Pack.Level.Other", "Other", 0, null);
        world.Spawn(joel, Suv, PoliceSuv, 0, 0, 0);

        int mine = world.Transport.SentTo(siriuss.Connection).Count;
        Sit(siriuss, Suv, 0);

        Assert.Equal(2, seen.Count);
        Assert.Equal(1, StandUps(world, siriuss, mine, Suv, 0));
    }

    [Fact]
    public void Twenty_attempts_a_second_for_four_seconds_stand_the_rider_up_once_a_window()
    {
        var (world, _, siriuss, _) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        var seen = OnlyJoelDrives(world);
        int mine = world.Transport.SentTo(siriuss.Connection).Count;

        for (var attempt = 0; attempt <= 80; attempt++)
        {
            Sit(siriuss, Suv, 0);
            world.Advance(TimeSpan.FromMilliseconds(50));
        }

        // Four seconds of asking at the rate Fusion's trigger sends: one at 0 s, 2 s and 4 s.
        Assert.Equal(3, seen.Count);
        Assert.Equal(3, StandUps(world, siriuss, mine, Suv, 0));
    }

    [Fact]
    public void The_refusal_line_counts_what_it_suppressed()
    {
        var (world, _, siriuss, _) = PoliceSuvOwnedByJoel();
        using var _2 = world;
        OnlyJoelDrives(world);

        Sit(siriuss, Suv, 0);
        Sit(siriuss, Suv, 0);
        Sit(siriuss, Suv, 0);
        world.Advance(TimeSpan.FromSeconds(2));
        Sit(siriuss, Suv, 0);

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"A plugin refused siriuss seat 0 of entity {Suv}: " +
                              "that driver seat is for listed gang roles only (2 more attempts suppressed before this)");
    }
}
