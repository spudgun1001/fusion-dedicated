using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using Steamworks;

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
        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy()));

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

    private static HSteamNetConnection AskWith(World world, ulong platformId, string name, byte[] stats)
    {
        var connection = world.Transport.Connect();
        world.Transport.Deliver(connection, ClientMessages.Join(world.Server.Config, platformId, name, stats));
        world.Server.Receive();
        world.Sync();

        return connection;
    }

    /// <summary>Whether any player-create or catch-up message naming this player went down the connection.</summary>
    private static bool ToldAbout(World world, HSteamNetConnection connection, ulong platformId)
        => world.Transport.SentTo(connection)
            .Select(sent => Envelope.Read(sent.Message))
            .OfType<Envelope>()
            .Any(envelope => envelope.Tag == FusionProtocol.TagConnectionResponse
                             && new FusionNetReader(envelope.Payload).ReadUInt64() == platformId);

    private static byte[] HeavyTotal()
    {
        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "massTotal", 100000000f);

        return stats;
    }

    [Fact]
    public void A_join_with_impossible_stats_is_refused_and_nobody_hears_of_it()
    {
        using var world = new World(Config());
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        var connection = AskWith(world, KanzaId, "Kanza", HeavyTotal());

        Assert.Null(world.Server.Players.GetByPlatformId(KanzaId));
        Assert.Single(world.Server.Players.Players);
        Assert.Equal("impossible avatar stats", ConnectionCloseTests.RefusalSentTo(world, connection));
        Assert.False(ToldAbout(world, joel.Connection, KanzaId));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message.StartsWith($"Rejected {KanzaId}: Kanza joined with an avatar with massTotal 100000000", StringComparison.Ordinal)
                 && e.Message.EndsWith(", refused", StringComparison.Ordinal));

        world.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Contains(world.Transport.Closed, c => c.Connection == connection.m_HSteamNetConnection);

        var max = AskWith(world, MaxId, "Max", ClientMessages.AvatarStats());

        Assert.NotNull(world.Server.Players.GetByPlatformId(MaxId));
        Assert.False(ToldAbout(world, max, KanzaId));
    }

    [Fact]
    public void A_normal_join_is_let_in_and_its_stats_are_kept()
    {
        using var world = new World(Config());
        byte[] stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "height", 1.8f);

        AskWith(world, KanzaId, "Kanza", stats);

        Assert.Equal(stats, world.Server.Players.GetByPlatformId(KanzaId)!.AvatarStats);
    }

    [Fact]
    public void A_join_with_impossible_stats_is_let_in_while_extended_protection_is_off()
    {
        var config = Config();
        config.ExtendedProtection = false;
        using var world = new World(config);

        AskWith(world, KanzaId, "Kanza", HeavyTotal());

        Assert.NotNull(world.Server.Players.GetByPlatformId(KanzaId));
    }

    private static byte[] OverTheLimit()
    {
        var stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "massTotal", 6000f);

        return stats;
    }

    private static float StatOf(byte[] stats, string field)
        => System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(
            stats.AsSpan(global::FusionDedicated.Server.Safety.AvatarStatsCheck.FieldNames.ToList().IndexOf(field) * 4, 4));

    [Fact]
    public void A_swap_merely_over_the_limits_is_clamped_without_a_strike()
    {
        using var world = new World(Config());
        var (joel, kanza, max) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;

        for (var i = 0; i < 5; i++)
        {
            joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, OverTheLimit()));
        }

        Assert.Equal(5, AvatarsTo(world, kanza, toKanza));
        Assert.False(Kicked(world, JoelId));
        Assert.Equal(5000f, StatOf(StatsTo(world, kanza, toKanza)!, "massTotal"));
        Assert.Equal(5000f, StatOf(world.Server.Players.GetByPlatformId(JoelId)!.AvatarStats, "massTotal"));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message == "Joel sent an avatar with massTotal 6000, over 5000, clamped to the limits");
    }

    [Fact]
    public void A_tiny_avatar_reaches_everyone_with_its_scale_clamped()
    {
        var config = Config();
        config.MinAvatarScale = 0.05f;
        using var world = new World(config);
        var (joel, kanza, max) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;
        int toMax = world.Transport.SentTo(max.Connection).Count;
        byte[] tiny = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(tiny, "localScale.x", 0.01f);

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, tiny));

        Assert.Equal(1, AvatarsTo(world, kanza, toKanza));
        Assert.Equal(1, AvatarsTo(world, max, toMax));
        Assert.Equal(0.05f, StatOf(StatsTo(world, kanza, toKanza)!, "localScale.x"));
        Assert.Equal(0.05f, StatOf(StatsTo(world, max, toMax)!, "localScale.x"));
        Assert.Equal(Barcode, world.Server.Players.GetByPlatformId(JoelId)!.AvatarBarcode);
        Assert.Equal(0.05f, StatOf(world.Server.Players.GetByPlatformId(JoelId)!.AvatarStats, "localScale.x"));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message == "Joel sent an avatar with localScale.x 0.01, under 0.05, clamped to the limits");
    }

    /// <summary>The proportions block inside the last avatar message this player received.</summary>
    private static byte[]? StatsTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Where(sent => sent.Message[0] == GateProtocol.TagPlayerRepAvatar)
            .Select(sent => GateProtocol.TryReadAvatarStats(sent.Message))
            .LastOrDefault();

    [Fact]
    public void A_swap_is_dropped_while_the_scale_limits_are_the_wrong_way_round()
    {
        var config = Config();
        config.MinAvatarScale = 2f;
        config.MaxAvatarScale = 1f;
        using var world = new World(config);
        var (joel, kanza, max) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;
        int toMax = world.Transport.SentTo(max.Connection).Count;
        byte[] stats = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(stats, "localScale.x", 5f);

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, stats));

        Assert.Equal(0, AvatarsTo(world, kanza, toKanza));
        Assert.Equal(0, AvatarsTo(world, max, toMax));
        Assert.False(Kicked(world, JoelId));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message == "Joel sent an avatar with localScale.x 5, over 1, dropped");
    }

    [Fact]
    public void A_late_joiner_is_told_the_clamped_stats()
    {
        var config = Config();
        config.MinAvatarScale = 0.05f;
        using var world = new World(config);
        var (joel, _, _) = Loaded(world);
        byte[] tiny = ClientMessages.AvatarStats();
        ClientMessages.SetAvatarStat(tiny, "localScale.x", 0.01f);

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, tiny));

        var latecomer = AskWith(world, 76561198000000005, "Late", ClientMessages.AvatarStats());
        byte[] clamped = world.Server.Players.GetByPlatformId(JoelId)!.AvatarStats;
        var told = world.Transport.SentTo(latecomer)
            .Select(sent => Envelope.Read(sent.Message))
            .OfType<Envelope>()
            .Last(envelope => envelope.Tag == FusionProtocol.TagConnectionResponse
                              && System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(envelope.Payload) == JoelId);

        Assert.Equal(0.05f, StatOf(clamped, "localScale.x"));
        Assert.True(told.Payload.AsSpan().IndexOf(clamped) > 0);
        Assert.True(told.Payload.AsSpan().IndexOf(tiny) < 0);
    }

    [Fact]
    public void A_join_merely_over_the_limits_is_let_in_with_its_stats_clamped()
    {
        using var world = new World(Config());
        var joel = world.Join(JoelId, "Joel");

        AskWith(world, KanzaId, "Kanza", OverTheLimit());

        var kanza = world.Server.Players.GetByPlatformId(KanzaId);
        Assert.NotNull(kanza);
        Assert.Equal(5000f, StatOf(kanza.AvatarStats, "massTotal"));
        Assert.Null(global::FusionDedicated.Server.Safety.AvatarStatsCheck.Problem(kanza.AvatarStats, world.Server.Config));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN" && e.Message == "Kanza joined with an avatar with massTotal 6000, over 5000, clamped to the limits");

        var told = world.Transport.SentTo(joel.Connection)
            .Select(sent => Envelope.Read(sent.Message))
            .OfType<Envelope>()
            .Last(envelope => envelope.Tag == FusionProtocol.TagConnectionResponse
                              && System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(envelope.Payload) == KanzaId);
        Assert.True(told.Payload.AsSpan().IndexOf(kanza.AvatarStats) > 0);
    }

    [Fact]
    public void Swaps_to_everyone_including_the_sender_are_gated_too()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);
        int toJoel = world.Transport.SentTo(joel.Connection).Count;
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ToClients(joel, Heavy()));

        Assert.Equal(0, AvatarsTo(world, joel, toJoel));
        Assert.Equal(0, AvatarsTo(world, kanza, toKanza));

        joel.Send(ToClients(joel, ClientMessages.AvatarStats()));

        Assert.Equal(1, AvatarsTo(world, joel, toJoel));
        Assert.Equal(1, AvatarsTo(world, kanza, toKanza));
    }

    private static byte[] ToClients(FakePlayer player, byte[] stats)
    {
        byte[] message = ClientMessages.Avatar(player.SmallId, Barcode, stats);
        message[1] = 2;

        return message;
    }

    [Fact]
    public void Swaps_are_not_checked_while_extended_protection_is_off()
    {
        var config = Config();
        config.ExtendedProtection = false;
        using var world = new World(config);
        var (joel, kanza, _) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy()));

        Assert.Equal(1, AvatarsTo(world, kanza, toKanza));
    }

    [Theory]
    [InlineData(59.9, true)]
    [InlineData(60, false)]
    public void A_strike_a_full_window_old_no_longer_counts(double thirdAfterSeconds, bool kicked)
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);

        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
        world.Advance(TimeSpan.FromSeconds(30));
        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));
        world.Advance(TimeSpan.FromSeconds(thirdAfterSeconds - 30));
        joel.Send(ClientMessages.Avatar(joel.SmallId, Barcode, Heavy(), target: kanza.SmallId));

        Assert.Equal(kicked, Kicked(world, JoelId));
    }

    [Fact]
    public void A_swap_whose_barcode_does_not_parse_is_dropped()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;

        var payload = new BonelabServerBrowser.Fusion.FusionNetWriter(512);
        payload.WriteRaw(ClientMessages.AvatarStats());
        payload.Write(1000);

        var message = new BonelabServerBrowser.Fusion.FusionNetWriter(600);
        message.Write(GateProtocol.TagPlayerRepAvatar);
        message.Write((byte)3);
        message.Write((byte)0);
        message.WriteNullable(joel.SmallId);
        message.WriteBlock(payload.ToArray());

        joel.Send(message.ToArray());

        Assert.Equal(0, AvatarsTo(world, kanza, toKanza));
        Assert.False(Kicked(world, JoelId));
    }
}
