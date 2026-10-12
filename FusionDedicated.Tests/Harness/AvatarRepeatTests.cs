using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>An avatar sent again unchanged within a short window reaches nobody, at every rank.</summary>
public class AvatarRepeatTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;
    private const ulong MaxId = 76561198000000003;

    private const string Leo = "spudgun1001.Suits.Avatar.LEO04";
    private const string Robot = "Pack.Avatar.Robot";

    // Anti-spam off, so the allowance cannot be what drops them. The owner was exempt from it on 2026-10-11.
    private static ServerConfig Config(int repeatSeconds = 2) => new()
    {
        CullOrphanedEntities = false,
        AntiSpamEnabled = false,
        AvatarRepeatSeconds = repeatSeconds,
    };

    private static (FakePlayer Joel, FakePlayer Kanza) Loaded(World world)
    {
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        return (joel, kanza);
    }

    private static byte[] Wear(FakePlayer player, string barcode, byte[]? stats = null)
        => ClientMessages.Avatar(player.SmallId, barcode, stats ?? ClientMessages.AvatarStats());

    private static int AvatarsTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(sent => sent.Message[0] == GateProtocol.TagPlayerRepAvatar);

    [Fact]
    public void The_first_avatar_is_relayed()
    {
        using var world = new World(Config());
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(Wear(joel, Leo));

        Assert.Equal(1, AvatarsTo(world, kanza, before));
    }

    [Fact]
    public void A_repeat_within_the_window_is_dropped()
    {
        using var world = new World(Config());
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(Wear(joel, Leo));
        world.Advance(TimeSpan.FromMilliseconds(70));
        joel.Send(Wear(joel, Leo));
        world.Advance(TimeSpan.FromMilliseconds(70));
        joel.Send(Wear(joel, Leo));

        Assert.Equal(1, AvatarsTo(world, kanza, before));
    }

    [Fact]
    public void A_different_barcode_or_new_stats_within_the_window_are_relayed()
    {
        using var world = new World(Config());
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        var taller = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(taller, "height", 2.1f);

        joel.Send(Wear(joel, Leo));
        joel.Send(Wear(joel, Robot));
        joel.Send(Wear(joel, Robot, taller));

        Assert.Equal(3, AvatarsTo(world, kanza, before));
    }

    [Fact]
    public void A_repeat_after_the_window_is_relayed()
    {
        using var world = new World(Config());
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(Wear(joel, Leo));
        world.Advance(TimeSpan.FromSeconds(2));
        joel.Send(Wear(joel, Leo));

        Assert.Equal(2, AvatarsTo(world, kanza, before));
    }

    [Fact]
    public void Zero_turns_the_check_off()
    {
        using var world = new World(Config(repeatSeconds: 0));
        var (joel, kanza) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(Wear(joel, Leo));
        joel.Send(Wear(joel, Leo));
        joel.Send(Wear(joel, Leo));

        Assert.Equal(3, AvatarsTo(world, kanza, before));
    }

    [Fact]
    public void A_late_joiner_still_gets_the_current_avatar()
    {
        using var world = new World(Config());
        var (joel, _) = Loaded(world);

        joel.Send(Wear(joel, Robot));
        joel.Send(Wear(joel, Leo));
        joel.Send(Wear(joel, Leo));

        var max = world.Join(MaxId, "Max");

        Assert.Equal(Leo, max.View.Players[joel.SmallId].AvatarBarcode);
    }

    [Fact]
    public void Drops_are_summed_up_once_a_minute()
    {
        using var world = new World(Config());
        var (joel, _) = Loaded(world);

        joel.Send(Wear(joel, Leo));
        joel.Send(Wear(joel, Leo));
        joel.Send(Wear(joel, Leo));
        world.Tick();

        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.StartsWith("Dropped 2 repeated", StringComparison.Ordinal));

        world.Advance(TimeSpan.FromMinutes(1));
        world.Tick();

        Assert.Single(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message == $"Dropped 2 repeated avatar messages from Joel ({Leo})");
    }
}
