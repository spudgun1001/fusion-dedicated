using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Scenarios;

/// <summary>Scenarios 2 and 7: a client sending thousands of refused spawns a second.</summary>
public class FloodScenario
{
    private static ServerConfig Locked() => new() { CullOrphanedEntities = false, Spawning = PermissionLevel.Operator };

    private static IEnumerable<byte[]> Flood(FakePlayer flooder, int count)
        => Enumerable.Range(0, count).Select(i => FusionProtocol.BuildSpawnRequest(
            flooder.SmallId, "SLZ.BONELAB.Content.Avatar.FordBW", new Vec3(0, 0, 0), (uint)i));

    [Fact]
    public void A_flooder_is_kicked_and_the_log_stays_short()
    {
        using var world = new World(Locked());
        var freeman = world.Join(76561198000000009, "Mr.freeman");
        freeman.FinishLoading();

        freeman.SendMany(Flood(freeman, 3400));

        Assert.Null(world.Server.Players.GetByPlatformId(76561198000000009));
        Assert.True(world.Server.RecentLog(2000).Count(e => e.Message.Contains("denied")) <= 5);
    }

    [Fact]
    public void A_player_who_rejoins_during_a_flood_agrees_with_everyone_on_owners()
    {
        using var world = new World(Locked());
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(kanza, 300, "Pack.Spawnable.Crate", 1, 2, 3);
        world.Spawn(kanza, 301, "Pack.Spawnable.Crate", 4, 5, 6);

        world.Leave(joel, "SteamNetworkingSockets shutdown");

        var freeman = world.Join(76561198000000009, "Mr.freeman");
        freeman.FinishLoading();

        // Joel's rejoin and loading are handled in the same receive batches as the flood.
        foreach (byte[] spam in Flood(freeman, 1700))
        {
            world.Transport.Deliver(freeman.Connection, spam);
        }

        joel = world.Join(76561198000000001, "Joel");

        foreach (byte[] spam in Flood(freeman, 1700))
        {
            world.Transport.Deliver(freeman.Connection, spam);
        }

        joel.FinishLoading();
        world.Advance(TimeSpan.FromSeconds(10));

        Assert.True(world.AgreeOnOwner(300), string.Join(", ", world.OwnersOf(300)));
        Assert.True(world.AgreeOnOwner(301), string.Join(", ", world.OwnersOf(301)));
    }
}
