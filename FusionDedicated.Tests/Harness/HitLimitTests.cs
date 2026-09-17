using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A client holding another player sent them a hit on every physics tick, which knocked them out.
/// Hits on a player you are holding are dropped, and so are hits over a per-second allowance.
/// </summary>
public class HitLimitTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;
    private const ulong DennisId = 76561198000000003;

    private static ServerConfig Limits(int hits = 3) => new()
    {
        CullOrphanedEntities = false,
        HitsPerSecond = hits,
    };

    private static FakePlayer Loaded(World world, ulong id, string name)
    {
        var player = world.Join(id, name);
        player.FinishLoading();

        return player;
    }

    private static int HitsTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(sent => Envelope.Read(sent.Message) is { Tag: GateProtocol.TagPlayerRepDamage });

    private static int Sent(World world, FakePlayer player) => world.Transport.SentTo(player.Connection).Count;

    [Fact]
    public void Hits_over_the_allowance_in_a_second_are_dropped()
    {
        using var world = new World(Limits(hits: 3));
        var joel = Loaded(world, JoelId, "Joel");
        var kanza = Loaded(world, KanzaId, "Kanza");
        int before = Sent(world, kanza);

        for (var i = 0; i < 5; i++)
        {
            joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 1.2f));
        }

        Assert.Equal(3, HitsTo(world, kanza, before));

        world.Advance(TimeSpan.FromSeconds(1));
        joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 1.2f));

        Assert.Equal(4, HitsTo(world, kanza, before));
    }

    [Fact]
    public void Each_target_has_its_own_allowance()
    {
        using var world = new World(Limits(hits: 3));
        var joel = Loaded(world, JoelId, "Joel");
        var kanza = Loaded(world, KanzaId, "Kanza");
        var dennis = Loaded(world, DennisId, "Dennis");
        int kanzaBefore = Sent(world, kanza);
        int dennisBefore = Sent(world, dennis);

        for (var i = 0; i < 3; i++)
        {
            joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 5f));
            joel.Send(ClientMessages.Damage(joel.SmallId, dennis.SmallId, 5f));
        }

        Assert.Equal(3, HitsTo(world, kanza, kanzaBefore));
        Assert.Equal(3, HitsTo(world, dennis, dennisBefore));
    }

    [Fact]
    public void Zero_hits_a_second_means_no_limit()
    {
        using var world = new World(Limits(hits: 0));
        var joel = Loaded(world, JoelId, "Joel");
        var kanza = Loaded(world, KanzaId, "Kanza");
        int before = Sent(world, kanza);

        for (var i = 0; i < 50; i++)
        {
            joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 1f));
        }

        Assert.Equal(50, HitsTo(world, kanza, before));
    }

    [Fact]
    public void A_player_holding_another_cannot_hurt_them_until_they_let_go()
    {
        using var world = new World(Limits(hits: 0));
        var joel = Loaded(world, JoelId, "Joel");
        var kanza = Loaded(world, KanzaId, "Kanza");
        var dennis = Loaded(world, DennisId, "Dennis");
        int before = Sent(world, kanza);

        joel.Grab(kanza.SmallId);
        joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 1.2f));
        dennis.Send(ClientMessages.Damage(dennis.SmallId, kanza.SmallId, 1.2f));

        Assert.Equal(1, HitsTo(world, kanza, before));

        joel.Send(FusionProtocol.BuildRelease(joel.SmallId, FusionProtocol.Handedness.RIGHT));
        joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 1.2f));

        Assert.Equal(2, HitsTo(world, kanza, before));
    }

    [Fact]
    public void Dropped_hits_are_summed_up_in_one_line_a_minute()
    {
        using var world = new World(Limits(hits: 1));
        var joel = Loaded(world, JoelId, "Joel");
        var kanza = Loaded(world, KanzaId, "Kanza");

        joel.Grab(kanza.SmallId);
        joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 1.2f));
        joel.Send(FusionProtocol.BuildRelease(joel.SmallId, FusionProtocol.Handedness.RIGHT));

        for (var i = 0; i < 4; i++)
        {
            joel.Send(ClientMessages.Damage(joel.SmallId, kanza.SmallId, 1.2f));
        }

        world.Advance(TimeSpan.FromMinutes(1));
        world.Tick();

        Assert.Single(world.Server.RecentLog(2000),
            e => e.Message == "Dropped 4 hits from Joel on Kanza in the last minute, 1 while holding them");
    }

    [Fact]
    public void A_held_player_does_not_show_as_an_item_in_hand()
    {
        using var world = new World(Limits());
        var joel = Loaded(world, JoelId, "Joel");
        var kanza = Loaded(world, KanzaId, "Kanza");

        joel.Grab(kanza.SmallId);

        Assert.Empty(world.Server.HeldBy(JoelId));
    }

    [Fact]
    public void A_player_who_left_is_no_longer_held_so_whoever_gets_their_id_can_be_hit()
    {
        using var world = new World(Limits(hits: 0));
        var joel = Loaded(world, JoelId, "Joel");
        var kanza = Loaded(world, KanzaId, "Kanza");

        joel.Grab(kanza.SmallId);
        world.Leave(kanza, "left");
        var dennis = Loaded(world, DennisId, "Dennis");
        Assert.Equal(kanza.SmallId, dennis.SmallId);
        int before = Sent(world, dennis);

        joel.Send(ClientMessages.Damage(joel.SmallId, dennis.SmallId, 1.2f));

        Assert.Equal(1, HitsTo(world, dennis, before));
    }
}
