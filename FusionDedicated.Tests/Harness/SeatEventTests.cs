using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Plugins are asked about every live seat. A refused ingress is kept from everybody else,
/// and the rider is stood up with an egress stamped as theirs and told who owns the vehicle.
/// </summary>
public class SeatEventTests
{
    private const ushort Car = 400;
    private const string PoliceCar = "spudgun1001.BabasPolice.Spawnable.SedanPolice";
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;
    private const ulong DennisId = 76561198000000003;

    private static (World World, FakePlayer Joel, FakePlayer Kanza, FakePlayer Dennis) PoliceCarOwnedByJoel()
    {
        var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        var dennis = world.Join(DennisId, "Dennis");

        foreach (var player in world.Players)
        {
            player.FinishLoading();
            player.View.DriverLockedVehicles.Add(Car);
        }

        world.Spawn(joel, Car, PoliceCar, 0, 0, 0);

        return (world, joel, kanza, dennis);
    }

    private static PluginEvents OnlyJoelDrives(World world, List<string>? logged = null)
    {
        var events = new PluginEvents(new PluginHealth(), (level, message) => logged?.Add($"{level} {message}"));

        events.Seat.Subscribe("gangs", e => e.Ingress && e.SeatIndex == 0 && e.PlatformId != JoelId
            ? PluginVerdict.Refuse("police only")
            : PluginVerdict.Allow);

        world.Server.Plugins = events;

        return events;
    }

    [Fact]
    public void A_plugin_is_told_the_rider_vehicle_barcode_and_seat()
    {
        var (world, _, kanza, _) = PoliceCarOwnedByJoel();
        using var __ = world;
        var seen = new List<SeatEvent>();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Seat.Subscribe("watcher", e => { seen.Add(e); return PluginVerdict.Allow; });
        world.Server.Plugins = events;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 1, true));

        var raised = Assert.Single(seen);
        Assert.Equal(KanzaId, raised.PlatformId);
        Assert.Equal(kanza.SmallId, raised.SmallId);
        Assert.Equal("Kanza", raised.Name);
        Assert.Equal(Car, raised.EntityId);
        Assert.Equal(PoliceCar, raised.Barcode);
        Assert.Equal((byte)1, raised.SeatIndex);
        Assert.True(raised.Ingress);
    }

    [Fact]
    public void A_refused_ingress_is_not_recorded_or_relayed_and_the_rider_is_stood_up()
    {
        var (world, joel, kanza, dennis) = PoliceCarOwnedByJoel();
        using var _ = world;
        OnlyJoelDrives(world);
        int joelBefore = world.Transport.SentTo(joel.Connection).Count;
        int kanzaBefore = world.Transport.SentTo(kanza.Connection).Count;
        int dennisBefore = world.Transport.SentTo(dennis.Connection).Count;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, true));

        Assert.Empty(world.Server.RidersOf(Car));
        Assert.Equal(joelBefore, world.Transport.SentTo(joel.Connection).Count);
        Assert.Equal(dennisBefore, world.Transport.SentTo(dennis.Connection).Count);

        var toKanza = world.Transport.SentTo(kanza.Connection).Skip(kanzaBefore).ToList();
        Assert.Equal(2, toKanza.Count);
        Assert.Equal(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, false), toKanza[0].Message);
        Assert.Equal(FusionProtocol.BuildOwnershipResponse(joel.SmallId, Car), toKanza[1].Message);
        Assert.All(toKanza, sent => Assert.True(sent.Reliable));

        Assert.False(kanza.View.Seats.ContainsKey(kanza.SmallId));
        Assert.False(dennis.View.Seats.ContainsKey(kanza.SmallId));
        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));
        Assert.Equal(joel.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
    }

    [Fact]
    public void A_refusal_is_in_the_detailed_log()
    {
        var (world, _, kanza, _) = PoliceCarOwnedByJoel();
        using var __ = world;
        OnlyJoelDrives(world);

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, true));

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"A plugin refused Kanza seat 0 of entity {Car}: police only");
    }

    [Fact]
    public void An_allowed_ingress_is_recorded_and_relayed_as_before()
    {
        var (world, joel, kanza, dennis) = PoliceCarOwnedByJoel();
        using var _ = world;
        OnlyJoelDrives(world);

        joel.Send(FusionProtocol.BuildSeat(joel.SmallId, Car, 0, true));
        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 1, true));

        Assert.Equal(new[] { joel.SmallId, kanza.SmallId }, world.Server.RidersOf(Car));
        Assert.Equal((Car, (byte)0), dennis.View.Seats[joel.SmallId]);
        Assert.Equal((Car, (byte)1), dennis.View.Seats[kanza.SmallId]);
    }

    [Fact]
    public void An_egress_is_raised_but_never_refused()
    {
        var (world, _, kanza, dennis) = PoliceCarOwnedByJoel();
        using var __ = world;
        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 1, true));

        var seen = new List<SeatEvent>();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Seat.Subscribe("stubborn", e => { seen.Add(e); return PluginVerdict.Refuse("stay"); });
        world.Server.Plugins = events;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 1, false));

        Assert.False(Assert.Single(seen).Ingress);
        Assert.Empty(world.Server.RidersOf(Car));
        Assert.False(dennis.View.Seats.ContainsKey(kanza.SmallId));
    }

    [Fact]
    public void A_handler_that_throws_allows_the_seat_and_is_logged()
    {
        var (world, _, kanza, dennis) = PoliceCarOwnedByJoel();
        using var __ = world;
        var logged = new List<string>();
        var events = new PluginEvents(new PluginHealth(), (level, message) => logged.Add($"{level} {message}"));
        events.Seat.Subscribe("broken", _ => throw new InvalidOperationException("boom"));
        world.Server.Plugins = events;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, true));

        Assert.Equal(new[] { kanza.SmallId }, world.Server.RidersOf(Car));
        Assert.Equal((Car, (byte)0), dennis.View.Seats[kanza.SmallId]);
        Assert.Contains("WARN Plugin 'broken' threw: boom", logged);
    }

    [Fact]
    public void A_catch_up_reply_is_not_put_to_plugins()
    {
        var (world, joel, kanza, _) = PoliceCarOwnedByJoel();
        using var __ = world;
        int raised = 0;
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Seat.Subscribe("counter", _ => { raised++; return PluginVerdict.Refuse("no"); });
        world.Server.Plugins = events;

        kanza.Send(CatchupSeat(answerer: kanza.SmallId, target: joel.SmallId, seatId: Car, index: 0));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_refused_ingress_clears_the_riders_old_seat_and_tells_the_others()
    {
        var (world, joel, kanza, dennis) = PoliceCarOwnedByJoel();
        using var _ = world;
        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 1, true));
        OnlyJoelDrives(world);
        int dennisBefore = world.Transport.SentTo(dennis.Connection).Count;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, true));
        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, Car, new Vec3(1, 2, 3), default, default, default));

        Assert.Empty(world.Server.RidersOf(Car));
        Assert.Equal(joel.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));

        var toDennis = world.Transport.SentTo(dennis.Connection).Skip(dennisBefore).ToList();
        Assert.Contains(toDennis, sent => sent.Message.SequenceEqual(FusionProtocol.BuildSeat(kanza.SmallId, Car, 1, false)));
        Assert.False(dennis.View.Seats.ContainsKey(kanza.SmallId));
        Assert.False(joel.View.Seats.ContainsKey(kanza.SmallId));
    }

    [Fact]
    public void An_egress_with_no_recorded_seat_is_not_put_to_plugins()
    {
        var (world, _, kanza, _) = PoliceCarOwnedByJoel();
        using var __ = world;
        int raised = 0;
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Seat.Subscribe("counter", _ => { raised++; return PluginVerdict.Allow; });
        world.Server.Plugins = events;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 1, false));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_refused_ingress_into_an_unknown_entity_sends_only_the_egress()
    {
        const ushort Unknown = 999;
        var (world, _, kanza, dennis) = PoliceCarOwnedByJoel();
        using var __ = world;
        var seen = new List<SeatEvent>();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Seat.Subscribe("refuser", e => { seen.Add(e); return PluginVerdict.Refuse("no"); });
        world.Server.Plugins = events;
        int kanzaBefore = world.Transport.SentTo(kanza.Connection).Count;
        int dennisBefore = world.Transport.SentTo(dennis.Connection).Count;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Unknown, 0, true));

        Assert.Equal("", Assert.Single(seen).Barcode);
        var toKanza = world.Transport.SentTo(kanza.Connection).Skip(kanzaBefore).ToList();
        Assert.Equal(FusionProtocol.BuildSeat(kanza.SmallId, Unknown, 0, false), Assert.Single(toKanza).Message);
        Assert.Equal(dennisBefore, world.Transport.SentTo(dennis.Connection).Count);
    }

    [Fact]
    public void A_refused_riders_later_egress_is_relayed_without_side_effects()
    {
        var (world, joel, kanza, dennis) = PoliceCarOwnedByJoel();
        using var _ = world;
        var seen = new List<SeatEvent>();
        var events = new PluginEvents(new PluginHealth(), (_, _) => { });
        events.Seat.Subscribe("gangs", e => { seen.Add(e); return PluginVerdict.Refuse("police only"); });
        world.Server.Plugins = events;

        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, true));
        int dennisBefore = world.Transport.SentTo(dennis.Connection).Count;
        kanza.Send(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, false));

        Assert.True(Assert.Single(seen).Ingress);
        Assert.Empty(world.Server.RidersOf(Car));
        Assert.Equal(joel.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        Assert.True(world.AgreeOnOwner(Car), string.Join(", ", world.OwnersOf(Car)));

        var toDennis = world.Transport.SentTo(dennis.Connection).Skip(dennisBefore).ToList();
        Assert.Equal(FusionProtocol.BuildSeat(kanza.SmallId, Car, 0, false), Assert.Single(toDennis).Message);
        Assert.False(dennis.View.Seats.ContainsKey(kanza.SmallId));
    }

    /// <summary>SeatExtender's catch-up reply: ToTarget, stamped with whoever answered.</summary>
    private static byte[] CatchupSeat(byte answerer, byte target, ushort seatId, byte index)
    {
        var data = new OracleWriter();
        data.Write(seatId);
        data.Write(index);
        data.Write(true);

        var message = new OracleWriter();
        message.Write((byte)8);
        message.Write((byte)4);
        message.Write((byte)0);
        message.Write((byte?)target);
        message.Write((byte?)answerer);
        message.Write(data.ToArray());

        return message.ToArray();
    }
}
