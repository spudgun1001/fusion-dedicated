using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>A client drops a variable on an entity it is still building, so one a plugin sets just after a spawn goes again at the window.</summary>
public class EarlyVariableResendTests
{
    private const ushort Id = 300;
    private static readonly string Path = RpcProtocol.PathFor(Id, 0);
    private static readonly string OtherPath = RpcProtocol.PathFor(Id, 1);

    private static World Spawned(ServerConfig? config = null, Action<FakePlayer>? beforeSpawn = null)
    {
        var world = new World(config);
        var a = world.Join(1, "A");
        var b = world.Join(2, "B");
        a.FinishLoading();
        b.FinishLoading();

        // Past the resends of owned props' variables that follow loading.
        Wait(world, 10);
        beforeSpawn?.Invoke(a);
        world.Spawn(a, Id, "Test.Crate", 0, 0, 0);
        return world;
    }

    private static void Wait(World world, double seconds) => world.Advance(TimeSpan.FromSeconds(seconds));

    private static int[] Marks(World world)
        => world.Players.Select(p => world.Transport.SentTo(p.Connection).Count).ToArray();

    /// <summary>The RPC bodies each player was sent since the marks.</summary>
    private static List<string>[] RpcsSince(World world, int[] marks)
        => world.Players.Select((p, i) => world.Transport.SentTo(p.Connection)
                .Skip(marks[i])
                .Select(m => m.Message)
                .Where(m => RpcProtocol.IsRpc(m[0]))
                .Select(m => Convert.ToHexString(GateProtocol.TryReadBody(m, m[0])!))
                .ToList())
            .ToArray();

    private static string Body(RpcKind kind, string path, RpcValue value)
        => Convert.ToHexString(RpcProtocol.WriteValue(kind, Convert.FromHexString(path), value));

    private static byte[] ClientInt(FakePlayer from, string path, int value)
        => GateProtocol.BuildRpcVariable((byte)RpcKind.Int, from.SmallId, from.SmallId,
            RpcProtocol.WriteValue(RpcKind.Int, Convert.FromHexString(path), RpcValue.OfInt(value)));

    [Fact]
    public void A_client_variable_on_a_young_entity_reaches_the_others_once_at_the_window_and_never_the_setter()
    {
        using var world = Spawned();
        var (a, b) = (world.Players[0], world.Players[1]);
        Wait(world, 0.5);
        a.Send(ClientInt(a, Path, 9));

        var marks = Marks(world);
        Wait(world, 2.4);
        Assert.All(RpcsSince(world, marks), Assert.Empty);

        Wait(world, 10);
        var sent = RpcsSince(world, marks);
        Assert.Empty(sent[0]);
        Assert.Equal(new[] { Body(RpcKind.Int, Path, RpcValue.OfInt(9)) }, sent[1]);
    }

    [Fact]
    public void The_resend_skips_the_client_that_set_a_value()
    {
        using var world = Spawned();
        var a = world.Players[0];
        a.Send(ClientInt(a, OtherPath, 9));
        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), null);

        var marks = Marks(world);
        Wait(world, 10);

        var plugin = Body(RpcKind.Int, Path, RpcValue.OfInt(5));
        var client = Body(RpcKind.Int, OtherPath, RpcValue.OfInt(9));
        var sent = RpcsSince(world, marks);

        Assert.Equal(new[] { plugin }, sent[0]);
        Assert.Equal(new[] { plugin, client }.Order(), sent[1].Order());
    }

    [Fact]
    public void A_resend_with_nothing_held_logs_nothing()
    {
        using var world = Spawned();

        // Too big to hold, so the cache has nothing for the entity when the resend runs.
        world.Server.SendRpc(RpcKind.String, Path, RpcValue.OfString(new string('Y', 600)), null);
        Wait(world, 10);

        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.Contains("early variable"));
    }

    [Fact]
    public void The_default_window_is_three_seconds()
        => Assert.Equal(3, new ServerConfig().EarlyVariableWindowSeconds);

    [Fact]
    public void A_variable_on_a_young_entity_is_resent_to_everyone_once_at_the_window()
    {
        using var world = Spawned();
        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), null);

        var marks = Marks(world);
        Wait(world, 2.4);
        Assert.All(RpcsSince(world, marks), Assert.Empty);

        Wait(world, 0.1);
        Assert.All(RpcsSince(world, marks), rpcs =>
            Assert.Equal(new[] { Body(RpcKind.Int, Path, RpcValue.OfInt(5)) }, rpcs));

        marks = Marks(world);
        Wait(world, 10);
        Assert.All(RpcsSince(world, marks), Assert.Empty);
    }

    [Fact]
    public void Two_variables_on_one_young_entity_share_one_resend()
    {
        using var world = Spawned();
        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), null);
        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Bool, OtherPath, RpcValue.OfBool(true), null);

        var marks = Marks(world);
        Wait(world, 10);

        var expected = new[]
        {
            Body(RpcKind.Int, Path, RpcValue.OfInt(5)),
            Body(RpcKind.Bool, OtherPath, RpcValue.OfBool(true)),
        };

        Assert.All(RpcsSince(world, marks), rpcs => Assert.Equal(expected.Order(), rpcs.Order()));
    }

    [Fact]
    public void A_variable_on_an_entity_older_than_the_window_is_not_resent()
    {
        using var world = Spawned();
        Wait(world, 3.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), null);

        var marks = Marks(world);
        Wait(world, 10);

        Assert.All(RpcsSince(world, marks), Assert.Empty);
    }

    [Fact]
    public void A_per_player_send_or_an_event_schedules_nothing()
    {
        // Held in the cache, so a resend scheduled by mistake would carry it. Set before the spawn so it schedules none itself.
        using var world = Spawned(beforeSpawn: a => a.Send(ClientInt(a, OtherPath, 9)));

        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), 1);
        world.Server.SendRpc(RpcKind.Event, Path, RpcValue.Nothing, null);

        var marks = Marks(world);
        Wait(world, 10);

        Assert.All(RpcsSince(world, marks), Assert.Empty);
    }

    [Fact]
    public void An_entity_removed_before_the_window_gets_no_resend()
    {
        using var world = Spawned();
        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), null);
        Wait(world, 0.5);
        world.Server.Entities.Remove(Id);

        // A new entity on the same id is not the one the resend was for.
        Wait(world, 1);
        world.Spawn(world.Players[0], Id, "Test.Crate", 0, 0, 0);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(7), null);

        var marks = Marks(world);
        Wait(world, 1);
        Assert.All(RpcsSince(world, marks), Assert.Empty);

        Wait(world, 2);
        Assert.All(RpcsSince(world, marks), rpcs =>
            Assert.Equal(new[] { Body(RpcKind.Int, Path, RpcValue.OfInt(7)) }, rpcs));
    }

    [Fact]
    public void A_window_of_zero_turns_resends_off()
    {
        using var world = Spawned(new ServerConfig { CullOrphanedEntities = false, EarlyVariableWindowSeconds = 0 });
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), null);

        var marks = Marks(world);
        Wait(world, 10);

        Assert.All(RpcsSince(world, marks), Assert.Empty);
    }

    [Fact]
    public void A_value_changed_before_the_resend_goes_out_as_the_latest()
    {
        using var world = Spawned();
        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(5), null);
        Wait(world, 0.5);
        world.Server.SendRpc(RpcKind.Int, Path, RpcValue.OfInt(6), null);

        var marks = Marks(world);
        Wait(world, 10);

        Assert.All(RpcsSince(world, marks), rpcs =>
            Assert.Equal(new[] { Body(RpcKind.Int, Path, RpcValue.OfInt(6)) }, rpcs));
    }
}
