using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Steam refused 8,895 sends in one evening and every one was thrown away, so ownership answers,
/// despawns and catch-up went missing and clients disagreed about the world. A refused reliable
/// message is held and sent again, and poses stop going to a player whose send buffer is full.
/// </summary>
public class SendPressureTests
{
    private const ushort Crate = 300;
    private const ushort Van = 1214;

    private static readonly ConnectionHealth Calm = new(PingMs: 60, QualityLocal: 1f, QualityRemote: 1f,
        OutBytesPerSecond: 40_000f, WaitingBytes: 1_000, QueueMicroseconds: 3_000, PendingBytes: 800);

    private static readonly ConnectionHealth Full = Calm with
    {
        WaitingBytes = 524_000,
        PendingBytes = 512_000,
        QueueMicroseconds = 1_995_000,
    };

    /// <summary>Long enough that the server measures every connection's send buffer again.</summary>
    private static readonly TimeSpan Pass = TimeSpan.FromMilliseconds(120);

    private static ServerConfig Config(int retryQueue = 256, int congestedBytes = 131_072) => new()
    {
        CullOrphanedEntities = false,
        SendRetryQueue = retryQueue,
        CongestedPendingBytes = congestedBytes,
    };

    private static byte[] Answer(byte owner) => FusionProtocol.BuildOwnershipResponse(owner, Crate);

    private static int AnswersTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection)
            .Count(sent => sent.Message[0] == FusionProtocol.TagEntityOwnershipResponse);

    private static int PosesTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection)
            .Count(sent => sent.Message[0] == FusionProtocol.TagEntityPoseUpdate);

    private static byte[] MovingPose(FakePlayer owner)
        => FusionProtocol.BuildEntityPoseUpdate(owner.SmallId, Crate, new Vec3(0, 0, 0), Quat.Identity,
            new Vec3(0, 1, 0), default);

    // ---- a refused reliable send is held and sent again ----

    [Fact]
    public void A_refused_reliable_send_is_sent_again_and_arrives()
    {
        using var world = new World(Config());
        var joel = world.Join(76561198000000001, "Joel");
        int before = AnswersTo(world, joel);

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        Assert.False(world.Server.SendTo(joel.Connection, Answer(joel.SmallId), reliable: true));
        Assert.Equal(before, AnswersTo(world, joel));

        world.Transport.FailSendsWith = null;
        world.Advance(Pass);

        Assert.Equal(before + 1, AnswersTo(world, joel));
    }

    [Fact]
    public void A_refused_unreliable_send_is_not_sent_again()
    {
        using var world = new World(Config());
        var joel = world.Join(76561198000000001, "Joel");
        int before = PosesTo(world, joel);

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.SendTo(joel.Connection, MovingPose(joel), reliable: false);

        world.Transport.FailSendsWith = null;
        world.Advance(Pass);

        Assert.Equal(before, PosesTo(world, joel));
    }

    [Fact]
    public void Reliable_messages_keep_their_order_when_one_was_refused()
    {
        using var world = new World(Config());
        var joel = world.Join(76561198000000001, "Joel");
        int before = world.Transport.SentTo(joel.Connection).Count;

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.SendTo(joel.Connection, Answer(1), reliable: true);
        world.Transport.FailSendsWith = null;

        // Sent after the refusal, so it has to wait behind it rather than overtake it.
        world.Server.SendTo(joel.Connection, Answer(2), reliable: true);
        world.Advance(Pass);

        var owners = world.Transport.SentTo(joel.Connection).Skip(before)
            .Select(sent => FusionProtocol.TryReadOwnershipResponse(sent.Message))
            .Where(read => read is { EntityId: Crate })
            .Select(read => read!.Value.PlayerId)
            .ToList();

        Assert.Equal(new byte[] { 1, 2 }, owners);
    }

    [Fact]
    public void A_full_retry_queue_drops_the_oldest_and_says_so_once()
    {
        using var world = new World(Config(retryQueue: 2));
        var joel = world.Join(76561198000000001, "Joel");
        int before = AnswersTo(world, joel);

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";

        for (byte owner = 1; owner <= 5; owner++)
        {
            world.Server.SendTo(joel.Connection, Answer(owner), reliable: true);
        }

        Assert.Single(world.Server.RecentLog(2000), e => e.Message.Contains("behind on the send queue"));

        world.Transport.FailSendsWith = null;
        world.Advance(Pass);

        Assert.Equal(before + 2, AnswersTo(world, joel));
    }

    [Fact]
    public void A_full_retry_queue_keeps_the_newest_messages()
    {
        using var world = new World(Config(retryQueue: 2));
        var joel = world.Join(76561198000000001, "Joel");
        int before = world.Transport.SentTo(joel.Connection).Count;

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";

        for (byte owner = 1; owner <= 5; owner++)
        {
            world.Server.SendTo(joel.Connection, Answer(owner), reliable: true);
        }

        world.Transport.FailSendsWith = null;
        world.Advance(Pass);

        var owners = world.Transport.SentTo(joel.Connection).Skip(before)
            .Select(sent => FusionProtocol.TryReadOwnershipResponse(sent.Message))
            .Where(read => read is { EntityId: Crate })
            .Select(read => read!.Value.PlayerId)
            .ToList();

        Assert.Equal(new byte[] { 4, 5 }, owners);
    }

    [Fact]
    public void Turning_the_queue_off_lets_nothing_overtake_what_is_already_held()
    {
        using var world = new World(Config(retryQueue: 4));
        var joel = world.Join(76561198000000001, "Joel");
        int before = world.Transport.SentTo(joel.Connection).Count;

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";

        for (byte owner = 1; owner <= 3; owner++)
        {
            world.Server.SendTo(joel.Connection, Answer(owner), reliable: true);
        }

        world.Transport.FailSendsWith = null;

        // The panel can do this mid-storm, and the messages already held still go out in order.
        world.Server.Config.SendRetryQueue = 0;
        world.Server.SendTo(joel.Connection, Answer(4), reliable: true);
        world.Advance(Pass);

        var owners = world.Transport.SentTo(joel.Connection).Skip(before)
            .Select(sent => FusionProtocol.TryReadOwnershipResponse(sent.Message))
            .Where(read => read is { EntityId: Crate })
            .Select(read => read!.Value.PlayerId)
            .ToList();

        Assert.Equal(new byte[] { 2, 3, 4 }, owners);
    }

    [Fact]
    public void A_kick_tells_the_player_why_even_with_a_backlog_waiting()
    {
        using var world = new World(Config());
        var joel = world.Join(76561198000000001, "Joel");

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.SendTo(joel.Connection, Answer(1), reliable: true);
        world.Transport.FailSendsWith = null;

        int before = world.Transport.SentTo(joel.Connection).Count;
        world.Server.Kick(joel.SmallId, "Flooding refused requests");

        var reason = System.Text.Encoding.UTF8.GetBytes("Flooding refused requests");

        Assert.Contains(world.Transport.SentTo(joel.Connection).Skip(before),
            sent => sent.Message[0] == FusionProtocol.TagDisconnect
                    && Contains(sent.Message, reason));
    }

    private static bool Contains(byte[] message, byte[] part)
    {
        for (var start = 0; start + part.Length <= message.Length; start++)
        {
            if (message.Skip(start).Take(part.Length).SequenceEqual(part))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void A_connection_that_keeps_refusing_does_not_inflate_the_refused_count()
    {
        using var world = new World(Config());
        var joel = world.Join(76561198000000001, "Joel");

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.SendTo(joel.Connection, Answer(1), reliable: true);

        long refused = world.Server.SendsRefused;

        for (var pass = 0; pass < 60; pass++)
        {
            world.Advance(TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(refused, world.Server.SendsRefused);
        Assert.Equal(60L, world.Server.RetriesRefused);

        world.Transport.FailSendsWith = null;
        world.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1L, world.Server.SendsRetried);
    }

    [Fact]
    public void A_queue_of_zero_drops_a_refused_send_where_it_stands()
    {
        using var world = new World(Config(retryQueue: 0));
        var joel = world.Join(76561198000000001, "Joel");
        int before = AnswersTo(world, joel);

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.SendTo(joel.Connection, Answer(joel.SmallId), reliable: true);
        world.Transport.FailSendsWith = null;

        world.Advance(Pass);

        Assert.Equal(before, AnswersTo(world, joel));
    }

    // ---- poses are shed while a connection is full ----

    private static (World World, FakePlayer Owner, FakePlayer Other) CrateAndTwoPlayers(ServerConfig config)
    {
        var world = new World(config);
        var owner = world.Join(76561198000000001, "Dennis");
        var other = world.Join(76561198000000002, "Joel");

        owner.FinishLoading();
        other.FinishLoading();

        world.Spawn(owner, Crate, "Test.Crate", 0, 0, 0);

        return (world, owner, other);
    }

    [Fact]
    public void Poses_stop_while_a_connection_is_full_and_start_again_when_it_drains()
    {
        var (world, owner, other) = CrateAndTwoPlayers(Config());
        using var _ = world;

        world.Transport.SetHealth(other.Connection, Full);
        world.Advance(Pass);

        int held = PosesTo(world, other);
        owner.Send(MovingPose(owner));

        Assert.Equal(held, PosesTo(world, other));

        world.Transport.SetHealth(other.Connection, Calm);
        world.Advance(Pass);
        owner.Send(MovingPose(owner));

        Assert.Equal(held + 1, PosesTo(world, other));
    }

    [Fact]
    public void The_player_whose_poses_are_held_back_is_named_once_a_minute()
    {
        var (world, _, other) = CrateAndTwoPlayers(Config());
        using var _2 = world;

        world.Transport.SetHealth(other.Connection, Full);
        world.Advance(Pass);
        world.Advance(Pass);

        var line = Assert.Single(world.Server.RecentLog(2000),
            e => e.Message.Contains("so their poses are held back"));

        Assert.Equal("Joel has 512.0 KB unsent, so their poses are held back until it drains", line.Message);
        Assert.Equal("INFO", line.Level);
    }

    [Fact]
    public void Reliable_messages_are_never_shed_from_a_full_connection()
    {
        var (world, _, other) = CrateAndTwoPlayers(Config());
        using var _2 = world;

        world.Transport.SetHealth(other.Connection, Full);
        world.Advance(Pass);

        int before = AnswersTo(world, other);
        Assert.True(world.Server.SendTo(other.Connection, Answer(other.SmallId), reliable: true));

        Assert.Equal(before + 1, AnswersTo(world, other));
    }

    [Fact]
    public void A_threshold_of_zero_sends_poses_whatever_is_waiting()
    {
        var (world, owner, other) = CrateAndTwoPlayers(Config(congestedBytes: 0));
        using var _ = world;

        world.Transport.SetHealth(other.Connection, Full);
        world.Advance(Pass);

        int before = PosesTo(world, other);
        owner.Send(MovingPose(owner));

        Assert.Equal(before + 1, PosesTo(world, other));
    }

    [Fact]
    public void A_connection_under_the_threshold_keeps_its_poses()
    {
        var (world, owner, other) = CrateAndTwoPlayers(Config(congestedBytes: 600_000));
        using var _ = world;

        world.Transport.SetHealth(other.Connection, Full);
        world.Advance(Pass);

        int before = PosesTo(world, other);
        owner.Send(MovingPose(owner));

        Assert.Equal(before + 1, PosesTo(world, other));
    }

    [Fact]
    public void The_retries_and_the_held_back_poses_are_summed_up_once_a_minute()
    {
        var (world, owner, other) = CrateAndTwoPlayers(Config());
        using var _ = world;

        world.Transport.SetHealth(other.Connection, Full);
        world.Advance(Pass);

        for (var i = 0; i < 20; i++)
        {
            owner.Send(MovingPose(owner));
        }

        world.Transport.FailSendsWith = "k_EResultLimitExceeded";
        world.Server.SendTo(other.Connection, Answer(1), reliable: true);
        world.Transport.FailSendsWith = null;

        world.Advance(TimeSpan.FromMinutes(1));
        world.Tick();

        var line = Assert.Single(world.Server.RecentLog(4000),
            e => e.Message.StartsWith("Send pressure in the last minute:"));

        Assert.Equal("Send pressure in the last minute: 1 reliable message(s) sent again, " +
                     "0 retry(ies) refused, 0 dropped from full queues, 20 pose(s) held back (0.8 KB)",
            line.Message);
        Assert.Equal("INFO", line.Level);
    }

    // ---- the evening of 2026-09-18 ----

    [Fact]
    public void Nineteen_connections_at_the_cap_still_get_their_despawns_and_ownership_answers()
    {
        var config = Config();
        config.MaxPlayers = 20;

        using var world = new World(config);
        var players = new List<FakePlayer>();

        for (var i = 0; i < 19; i++)
        {
            players.Add(world.Join(76561198000000001 + (ulong)i, $"Player{i}"));
        }

        foreach (var player in players)
        {
            player.FinishLoading();
        }

        var sad = players[0];
        world.Spawn(sad, Van, "spudgun1001.BabasPolice.Spawnable.VanSWATTransport", 0, 0, 0);
        world.Spawn(sad, Crate, "Test.Crate", 5, 0, 0);

        // Every send buffer at Steam's 512 KB ceiling at the same instant, refusing everything.
        foreach (var player in players)
        {
            world.Transport.SetHealth(player.Connection, Full);
        }

        world.Advance(Pass);
        world.Transport.FailSendsWith = "k_EResultLimitExceeded";

        sad.Send(ClientMessages.Despawn(sad.SmallId, Van));
        players[1].Send(FusionProtocol.BuildOwnershipRequest(players[1].SmallId, Crate));

        world.Transport.FailSendsWith = null;

        foreach (var player in players)
        {
            world.Transport.SetHealth(player.Connection, Calm);
        }

        world.Advance(Pass);

        Assert.All(players, player => Assert.DoesNotContain(Van, player.View.Entities.Keys));
        Assert.All(world.OwnersOf(Crate).Values, owner => Assert.Equal(players[1].SmallId, owner));
        Assert.Equal(players[1].SmallId, world.Server.Entities.Get(Crate)!.OwnerSmallId);
    }

    [Fact]
    public void A_join_during_a_long_refusal_loses_no_catch_up()
    {
        var config = Config(retryQueue: 8);
        config.CatchupMessagesPerSecond = 100;

        using var world = new World(config);
        var dennis = world.Join(76561198000000001, "Dennis");
        dennis.FinishLoading();

        for (ushort id = 400; id < 500; id++)
        {
            world.Spawn(dennis, id, "Test.Crate", id, 0, 0);
        }

        // Steam takes nothing for four seconds, and the joiner is owed far more catch-up than
        // the queue of 8 could hold, so it only arrives if catch-up waits its turn instead.
        world.Transport.FailSendsWith = "k_EResultLimitExceeded";

        var joiner = world.Join(76561198000000002, "Joel");

        for (var pass = 0; pass < 250; pass++)
        {
            world.Advance(TimeSpan.FromMilliseconds(16));
        }

        world.Transport.FailSendsWith = null;
        joiner.FinishLoading();

        for (var pass = 0; pass < 250; pass++)
        {
            world.Advance(TimeSpan.FromMilliseconds(16));
        }

        Assert.Equal(100, joiner.View.Entities.Count);
        Assert.Equal(0, world.Server.RetriesDropped);
    }
}
