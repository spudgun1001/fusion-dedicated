using System.Buffers.Binary;
using System.Text;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Server.Safety;
using FusionDedicated.Tests.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>Messages whose prefix Fusion reads differently from the server are dropped, so clients only ever read what was checked.</summary>
public class MalformedPrefixTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;
    private const ulong MaxId = 76561198000000003;

    private const string Barcode = "SLZ.BONELAB.Content.Avatar.Heavy";

    private static ServerConfig Config() => new() { CullOrphanedEntities = false, AntiSpamEnabled = false };

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

    /// <summary>
    /// The review's bypass. The sender is sent as null, so stamping it used to overwrite the first byte
    /// of the length and clients read the block one byte later than the server checked it.
    /// </summary>
    private static byte[] NullSenderFling()
    {
        const uint sane = 0x3F3F3F3F;       // 0.747, and the same one byte along
        const uint heavy = 0x4CBEBC42;      // about 1e8 to a client, 51 or 0.8 to the server

        var stats = new byte[FusionProtocol.AvatarStatsSize];

        for (var i = 0; i < FusionProtocol.AvatarStatFloatCount; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(stats.AsSpan(i * 4, 4), i >= 99 ? heavy : sane);
        }

        byte[] barcode = Encoding.UTF8.GetBytes(Barcode);
        var clientPayload = new byte[575];
        stats.CopyTo(clientPayload, 0);
        BinaryPrimitives.WriteInt32BigEndian(clientPayload.AsSpan(420, 4), barcode.Length);
        barcode.CopyTo(clientPayload, 424);

        var message = new byte[9 + clientPayload.Length];
        message[0] = GateProtocol.TagPlayerRepAvatar;
        message[1] = 3;     // ToOtherClients
        message[2] = 0;     // Reliable
        message[3] = 0;     // Sender.HasValue false
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(5, 4), clientPayload.Length);
        message[4] = 0;
        clientPayload.CopyTo(message, 9);

        return message;
    }

    /// <summary>A normal swap with its route bytes replaced.</summary>
    private static byte[] WithRoute(byte[] route, byte[]? stats = null, int extraBytes = 0)
    {
        var payload = new FusionNetWriter(512);
        payload.WriteRaw(stats ?? ClientMessages.AvatarStats());
        payload.Write(Barcode);

        var message = new FusionNetWriter(600);
        message.Write(GateProtocol.TagPlayerRepAvatar);
        message.WriteRaw(route);
        message.WriteBlock(payload.ToArray());
        message.WriteRaw(new byte[extraBytes]);

        return message.ToArray();
    }

    private static IEnumerable<byte[]> AllSent(World world, params FakePlayer[] players)
        => players.SelectMany(p => world.Transport.SentTo(p.Connection)).Select(s => s.Message);

    private static int AvatarsTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection).Skip(before).Count(s => s.Message[0] == GateProtocol.TagPlayerRepAvatar);

    [Fact]
    public void The_null_sender_fling_really_shows_clients_a_hundred_million()
    {
        // The old stamp wrote HasValue and the sender over the null byte and the length's first byte.
        byte[] oldStamp = NullSenderFling();
        oldStamp[3] = 1;
        oldStamp[4] = 1;

        var client = OracleMessage.Read(oldStamp);

        Assert.NotNull(client);
        Assert.StartsWith("massArm ", AvatarStatsCheck.Problem(client.Value.Payload.AsSpan(0, 420), new ServerConfig()));
    }

    [Fact]
    public void The_null_sender_fling_reaches_nobody()
    {
        using var world = new World(Config());
        var (joel, kanza, max) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;
        int toMax = world.Transport.SentTo(max.Connection).Count;

        joel.Send(NullSenderFling());

        Assert.Equal(0, AvatarsTo(world, kanza, toKanza));
        Assert.Equal(0, AvatarsTo(world, max, toMax));
    }

    [Theory]
    [InlineData(new byte[] { 4, 0, 2, 2, 1, 1 })]          // target HasValue 2
    [InlineData(new byte[] { 3, 0, 2, 1 })]                // sender HasValue 2
    [InlineData(new byte[] { 5, 0, 0, 0, 0, 1, 2, 1, 1 }, 1)]  // ToTargets with a byte past the payload
    [InlineData(new byte[] { 3, 0, 1, 1 }, 3)]             // bytes past the payload
    public void A_swap_with_a_malformed_prefix_reaches_nobody(byte[] route, int extraBytes = 0)
    {
        using var world = new World(Config());
        var (joel, kanza, max) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;
        int toMax = world.Transport.SentTo(max.Connection).Count;

        joel.Send(WithRoute(route, extraBytes: extraBytes));

        Assert.Equal(0, AvatarsTo(world, kanza, toKanza));
        Assert.Equal(0, AvatarsTo(world, max, toMax));
        Assert.NotEqual(Barcode, world.Server.Players.GetByPlatformId(JoelId)!.AvatarBarcode);
    }

    [Fact]
    public void A_well_formed_swap_to_listed_targets_still_arrives()
    {
        using var world = new World(Config());
        var (joel, kanza, max) = Loaded(world);
        int toKanza = world.Transport.SentTo(kanza.Connection).Count;
        int toMax = world.Transport.SentTo(max.Connection).Count;

        joel.Send(WithRoute(new byte[] { 5, 0, 0, 0, 0, 1, kanza.SmallId, 1, joel.SmallId }));

        Assert.Equal(1, AvatarsTo(world, kanza, toKanza));
        Assert.Equal(0, AvatarsTo(world, max, toMax));
    }

    [Fact]
    public void Each_drop_goes_to_the_detailed_log_with_a_count_for_the_player()
    {
        using var world = new World(Config());
        var (joel, _, _) = Loaded(world);

        joel.Send(NullSenderFling());
        joel.Send(NullSenderFling());

        var lines = world.Server.RecentLog(2000).Where(e => e.Message.StartsWith("Dropped a malformed message from Joel")).ToList();

        Assert.Equal(2, lines.Count);
        Assert.EndsWith("(1 so far)", lines[0].Message);
        Assert.EndsWith("(2 so far)", lines[1].Message);
        Assert.Contains("tag 5", lines[0].Message);
    }

    [Fact]
    public void Malformed_drops_stay_off_the_console()
    {
        string body = FusionDedicated.Tests.Server.FusionServerSource.Text();
        int found = body.IndexOf("Dropped a malformed message from", StringComparison.Ordinal);
        int end = body.IndexOf("console: false);", found, StringComparison.Ordinal);
        int nextLog = body.IndexOf("Log(", found, StringComparison.Ordinal);

        Assert.True(found > 0 && end > found && (nextLog < 0 || end < nextLog));
    }

    [Fact]
    public void No_client_ever_reads_an_impossible_avatar()
    {
        var config = Config();
        config.AvatarStrikesBeforeKick = 0;
        using var world = new World(config);
        var (joel, kanza, max) = Loaded(world);
        byte[] heavy = ClientMessages.AvatarStats();

        foreach (string field in new[] { "massArm", "massChest", "massHead", "massLeg", "massPelvis", "massTotal" })
        {
            ClientMessages.SetAvatarStat(heavy, field, 100000000f);
        }

        var attempts = new List<byte[]>
        {
            NullSenderFling(),
            WithRoute(new byte[] { 4, 0, 2, kanza.SmallId, 1, 1 }),
            WithRoute(new byte[] { 3, 0, 2, 1 }),
            WithRoute(new byte[] { 3, 0, 1, joel.SmallId }, extraBytes: 3),
            ClientMessages.Avatar(joel.SmallId, Barcode, heavy),
            ClientMessages.Avatar(joel.SmallId, Barcode, heavy, target: kanza.SmallId),
            WithRoute(new byte[] { 2, 0, 1, joel.SmallId }, heavy),
            WithRoute(new byte[] { 5, 0, 0, 0, 0, 2, kanza.SmallId, max.SmallId, 1, joel.SmallId }, heavy),
            ClientMessages.Avatar(joel.SmallId, Barcode, ClientMessages.AvatarStats()),
        };

        foreach (byte[] attempt in attempts)
        {
            joel.Send(attempt);
        }

        var avatarsRead = AllSent(world, joel, kanza, max)
            .Select(OracleMessage.Read)
            .Where(m => m is { Tag: GateProtocol.TagPlayerRepAvatar })
            .Select(m => m!.Value)
            .ToList();

        Assert.NotEmpty(avatarsRead);

        foreach (var read in avatarsRead)
        {
            Assert.True(read.Payload.Length >= 420);
            Assert.Null(AvatarStatsCheck.Problem(read.Payload.AsSpan(0, 420), world.Server.Config));
        }
    }

    /// <summary>A module message as a mod sends it: the route carries no sender at all.</summary>
    private static byte[] ModuleWithNoSender(byte[] body)
    {
        var message = new FusionNetWriter(body.Length + 32);
        message.Write(ModuleProtocol.TagModule);
        message.Write((byte)3);     // ToOtherClients
        message.Write((byte)0);     // Reliable
        message.Write(false);       // Sender.HasValue
        message.WriteBlock(body);

        return message.ToArray();
    }

    [Fact]
    public void A_module_message_with_no_sender_reaches_the_other_players()
    {
        using var world = new World(Config());
        var (joel, kanza, _) = Loaded(world);
        byte[] body = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.Send(ModuleWithNoSender(body));

        var relayed = world.Transport.SentTo(kanza.Connection).Skip(before)
            .Select(sent => OracleMessage.Read(sent.Message))
            .OfType<OracleMessage>()
            .Where(read => read.Tag == ModuleProtocol.TagModule)
            .ToList();

        var read = Assert.Single(relayed);
        Assert.Equal(joel.SmallId, read.Sender);
        Assert.Equal(body, read.Payload);
        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.StartsWith("Dropped a malformed message"));
    }
}
