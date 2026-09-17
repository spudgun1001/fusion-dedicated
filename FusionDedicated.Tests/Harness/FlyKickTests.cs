using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Fly mods spawn their gun on the player's own machine, so the server only ever sees the rig
/// climbing. Enough of that is a kick, unless they are staff or sitting in something.
/// </summary>
public class FlyKickTests
{
    private const ulong JoelId = 76561198000000001;
    private const ushort Car = 400;

    private static ServerConfig Config() => new() { CullOrphanedEntities = false };

    /// <summary>Flies a player straight up at 8 m/s, at Fusion's 20 poses a second, with their ride if they have one.</summary>
    private static void FlyUp(World world, FakePlayer player, float seconds, ushort? ride = null)
    {
        for (var step = 1; step <= (int)(seconds * 20); step++)
        {
            world.Advance(TimeSpan.FromMilliseconds(50));
            float height = step / 20f * 8f;

            if (ride is { } vehicle)
            {
                player.Send(FusionProtocol.BuildEntityPoseUpdate(player.SmallId, vehicle,
                    new Vec3(0, height, 0), Quat.Identity, new Vec3(0, 8, 0), default));
            }

            player.Send(FusionProtocol.BuildPlayerPoseUpdate(player.SmallId,
                new FusionRigPose { PelvisPosition = new Vec3(0, height, 0) }));
        }
    }

    private static List<ServerLogEntry> FlightLines(World world)
        => world.Server.RecentLog(2000).Where(e => e.Message.Contains("looks like flying")).ToList();

    private static bool WasKicked(World world, FakePlayer player)
        => world.Transport.Closed.Any(c => c.Connection == player.Connection.m_HSteamNetConnection)
           || world.Server.Players.Get(player.SmallId)?.Kicked == true;

    [Fact]
    public void A_player_who_keeps_climbing_is_warned_and_then_kicked()
    {
        using var world = new World(Config());
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        FlyUp(world, joel, 8f);

        Assert.NotEmpty(FlightLines(world));
        Assert.Contains(FlightLines(world), e => e.Message.StartsWith("Joel climbed") && e.Level == "WARN");
        Assert.True(WasKicked(world, joel));
    }

    [Fact]
    public void Nobody_is_kicked_while_the_strikes_are_zero()
    {
        var config = Config();
        config.FlightStrikesBeforeKick = 0;

        using var world = new World(config);
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        FlyUp(world, joel, 8f);

        Assert.NotEmpty(FlightLines(world));
        Assert.False(WasKicked(world, joel));
    }

    [Fact]
    public void A_player_in_a_seat_is_left_alone()
    {
        using var world = new World(Config());
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, Car, "Test.Car", 0, 0, 0);
        joel.Send(FusionProtocol.BuildSeat(joel.SmallId, Car, 0, true));

        // A lift, a helicopter or a car up a ramp: the ride climbs with them.
        FlyUp(world, joel, 8f, Car);

        Assert.Empty(FlightLines(world));
        Assert.False(WasKicked(world, joel));
    }

    [Fact]
    public void Staff_are_left_alone()
    {
        using var world = new World(Config());
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        world.Server.Players.Get(joel.SmallId)!.Permission = PermissionLevel.Operator;

        FlyUp(world, joel, 8f);

        Assert.Empty(FlightLines(world));
        Assert.False(WasKicked(world, joel));
    }

    [Fact]
    public void Walking_about_is_left_alone()
    {
        using var world = new World(Config());
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        for (var step = 1; step <= 200; step++)
        {
            world.Advance(TimeSpan.FromMilliseconds(50));
            float height = step % 40 < 20 ? 0f : 1.1f;
            joel.Send(FusionProtocol.BuildPlayerPoseUpdate(joel.SmallId,
                new FusionRigPose { PelvisPosition = new Vec3(step * 0.2f, height, 0) }));
        }

        Assert.Empty(FlightLines(world));
    }
}
