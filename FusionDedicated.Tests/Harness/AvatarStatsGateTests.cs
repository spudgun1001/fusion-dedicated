using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>An avatar with impossible stats is dropped before anybody receives it, and a player who keeps sending them is kicked.</summary>
public class AvatarStatsGateTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;
    private const ulong MaxId = 76561198000000003;

    private const string Barcode = "SLZ.BONELAB.Content.Avatar.Heavy";

    private static ServerConfig Config(int strikes = 3) => new()
    {
        CullOrphanedEntities = false,
        AntiSpamEnabled = false,
        AvatarStrikesBeforeKick = strikes,
    };

    private static byte[] Heavy()
    {
        var stats = ClientMessages.AvatarStats();

        foreach (string field in new[] { "massArm", "massChest", "massHead", "massLeg", "massPelvis", "massTotal" })
        {
            ClientMessages.SetAvatarStat(stats, field, 100000000f);
        }

        return stats;
    }

    private static (FakePlayer Joel, FakePlayer Kanza, FakePlayer Max) Loaded(World world)
    {
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        var max = world.Join(MaxId, "Max");
        joel.FinishLoading();
        kanza.FinishLoading();
        max.FinishLoading();

        return (joel, kanza, max);
    }

    private static int AvatarsTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(sent => sent.Message[0] == GateProtocol.TagPlayerRepAvatar);

    private static bool Kicked(World world, ulong platformId)
        => world.Server.Players.GetByPlatformId(platformId) == null;

    [Fact]
    public void The_flinging_avatar_reaches_nobody_and_is_not_kept_for_catch_up()
    {
        using var world = new World(Config());
        var (joel, kanza, max) = Loaded(world);
        byte[] statsBefore = world.Server.Players.GetByPlatformId(JoelId)!.AvatarStats.ToArray();
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;
        int toMax = world.Transport.SentTo(max.Connection).Count;

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));

        Assert.Equal(0, AvatarsTo(world, kanza, toKanza));
        Assert.Equal(0, AvatarsTo(world, max, toMax));

        var joined = world.Server.Players.GetByPlatformId(JoelId)!;
        Assert.Equal(statsBefore, joined.AvatarStats);
        Assert.NotEqual(Barcode, joined.AvatarBarcode);

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message.StartsWith("Joel sent an avatar with massArm 100000000", StringComparison.Ordinal)
                 && e.Message.EndsWith(", dropped", StringComparison.Ordinal));
    }

    [Fact]
    public void An_avatar_message_too_short_for_the_stats_is_dropped()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ClientMessages.Avatar(joel.SmallId, "", new byte[40]));

        Assert.Equal(0, AvatarsTo(world, kanza, before));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message.StartsWith("Joel sent an avatar with ", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_bad_avatars_in_a_minute_do_not_kick()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));

        Assert.False(Kicked(world, JoelId));
    }

    [Fact]
    public void Three_bad_avatars_in_a_minute_kick_the_sender()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);

        for (var i = 0; i < 3; i++)
        {
            joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
            world.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.True(Kicked(world, JoelId));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Kicked Joel: Kicked for sending impossible avatar stats");
    }

    [Fact]
    public void Strikes_older_than_the_window_do_not_count()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);

        for (var i = 0; i < 3; i++)
        {
            joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
            world.Advance(TimeSpan.FromSeconds(31));
        }

        Assert.False(Kicked(world, JoelId));
    }

    [Fact]
    public void Zero_strikes_never_kicks()
    {
        using var world = new World(Config(strikes: 0));
        var (joel, kanza, _) = Loaded(world);

        for (var i = 0; i < 10; i++)
        {
            joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
        }

        Assert.False(Kicked(world, JoelId));
    }

    [Fact]
    public void A_player_who_leaves_takes_their_strikes_with_them()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
        world.Leave(joel, "Closing Connection");

        var newbie = world.Join(76561198000000004, "Newbie");
        Assert.Equal(joel.SmallId, newbie.SmallId);
        newbie.Send(ClientMessages.Avatar(newbie.SmallId, Barcode, Heavy(), target: kanza.SmallId));

        Assert.False(Kicked(world, 76561198000000004));
    }

    [Fact]
    public void A_normal_avatar_swap_still_reaches_everyone_and_is_kept()
    {
        using var world = new World(Config());
        var (joel, kanza, max) = Loaded(world);
        byte[] stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "localScale.y", 1.2f);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;
        int toMax = world.Transport.SentTo(max.Connection).Count;

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, stats));

        Assert.Equal(1, AvatarsTo(world, kanza, toKanza));
        Assert.Equal(1, AvatarsTo(world, max, toMax));

        var joined = world.Server.Players.GetByPlatformId(JoelId)!;
        Assert.Equal(stats, joined.AvatarStats);
        Assert.Equal(Barcode, joined.AvatarBarcode);
    }
}
