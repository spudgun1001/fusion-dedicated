using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A joiner is caught up at CatchupMessagesPerSecond. Whatever fits in the allowance goes
/// out at once and the rest follows as it refills, so a busy world does not arrive in one burst.
/// </summary>
public class PacedCatchupTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong LateId = 76561198000000002;

    /// <summary>Joel, loaded, with twelve crates, on a server with the given pace.</summary>
    private static (World World, FakePlayer Joel) TwelveCrates(int perSecond)
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = false, CatchupMessagesPerSecond = perSecond });
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        for (ushort id = 300; id < 312; id++)
        {
            world.Spawn(joel, id, "Pack.Spawnable.Crate", id, 0, 0);
        }

        return (world, joel);
    }

    private static int Sent(World world, FakePlayer player, byte tag, int skip = 0)
        => world.Transport.SentTo(player.Connection).Skip(skip).Count(s => s.Message[0] == tag);

    private static int RpcsTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection).Count(s => RpcProtocol.IsRpc(s.Message[0]));

    private static int PacingLines(World world, string name)
        => world.Server.RecentLog().Count(e => e.Message.StartsWith($"Pacing catch-up for {name}:", StringComparison.Ordinal));

    [Fact]
    public void A_joiner_gets_the_first_batch_at_once_and_the_rest_as_time_passes()
    {
        var (world, _) = TwelveCrates(perSecond: 5);
        using var disposing = world;

        var late = world.Join(LateId, "Late");
        Assert.Equal(5, Sent(world, late, FusionProtocol.TagSpawnResponse));

        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(10, Sent(world, late, FusionProtocol.TagSpawnResponse));

        world.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(12, Sent(world, late, FusionProtocol.TagSpawnResponse));

        for (ushort id = 300; id < 312; id++)
        {
            Assert.True(world.AgreeOnOwner(id), string.Join(", ", world.OwnersOf(id)));
        }
    }

    [Fact]
    public void A_catch_up_within_the_allowance_goes_out_at_once_and_is_not_logged()
    {
        var (world, _) = TwelveCrates(perSecond: 100);
        using var disposing = world;

        var late = world.Join(LateId, "Late");

        Assert.Equal(12, Sent(world, late, FusionProtocol.TagSpawnResponse));
        Assert.Equal(0, PacingLines(world, "Late"));
    }

    [Fact]
    public void A_paced_catch_up_is_logged_once()
    {
        var (world, _) = TwelveCrates(perSecond: 5);
        using var disposing = world;

        var late = world.Join(LateId, "Late");

        Assert.Contains(world.Server.RecentLog(), e => e.Message == "Pacing catch-up for Late: 7 messages waiting");

        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(1, PacingLines(world, "Late"));
    }

    [Fact]
    public void Level_variables_are_paced_the_same_way()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, CatchupMessagesPerSecond = 5 });
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        for (ushort id = 300; id < 312; id++)
        {
            world.Server.SendRpc(RpcKind.String, RpcProtocol.PathFor(id, 0), RpcValue.OfString($"v{id}"), null);
        }

        var late = world.Join(LateId, "Late");
        late.FinishLoading();
        Assert.Equal(5, RpcsTo(world, late));

        world.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(10, RpcsTo(world, late));

        world.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(12, RpcsTo(world, late));
    }

    [Fact]
    public void Cull_flags_sent_after_loading_are_paced_and_all_arrive()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, CatchupMessagesPerSecond = 1 });
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 0, 0);
        world.Spawn(joel, 301, "Pack.Spawnable.Crate", 2, 0, 0);
        joel.Send(ClientMessages.CullStatus(joel.SmallId, 300, true));
        joel.Send(ClientMessages.CullStatus(joel.SmallId, 301, true));

        // One spawn at once and one a second later.
        var late = world.Join(LateId, "Late");
        late.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(1));

        // The resend two seconds after loading has room for one of the two.
        world.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Sent(world, late, FusionProtocol.TagEntityCullStatus));

        world.Advance(TimeSpan.FromSeconds(10));
        Assert.True(late.View.Entities[300].CulledForOwner);
        Assert.True(late.View.Entities[301].CulledForOwner);
    }

    [Fact]
    public void A_player_who_leaves_mid_queue_is_sent_nothing_more()
    {
        var (world, _) = TwelveCrates(perSecond: 5);
        using var disposing = world;

        var late = world.Join(LateId, "Late");
        Assert.Equal(5, Sent(world, late, FusionProtocol.TagSpawnResponse));
        int sent = world.Transport.SentTo(late.Connection).Count;

        world.Leave(late, "Closing Connection");
        world.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(sent, world.Transport.SentTo(late.Connection).Count);
    }

    [Fact]
    public void A_level_change_sends_nothing_from_the_old_queues()
    {
        var (world, _) = TwelveCrates(perSecond: 5);
        using var disposing = world;

        var late = world.Join(LateId, "Late");
        Assert.Equal(5, Sent(world, late, FusionProtocol.TagSpawnResponse));

        world.Server.SetLevel("Pack.Level.Other", "Other", 0, null);
        int after = world.Transport.SentTo(late.Connection).Count;
        world.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, Sent(world, late, FusionProtocol.TagSpawnResponse, skip: after));
    }

    [Fact]
    public void A_pace_of_zero_sends_everything_at_once()
    {
        var (world, _) = TwelveCrates(perSecond: 0);
        using var disposing = world;

        var late = world.Join(LateId, "Late");

        Assert.Equal(12, Sent(world, late, FusionProtocol.TagSpawnResponse));
        Assert.Equal(0, PacingLines(world, "Late"));
    }
}
