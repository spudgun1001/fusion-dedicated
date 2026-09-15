using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Each constraint makes two entities, so it is held to the same per-second cap and spawn guard as a spawn.</summary>
public class ConstraintLimitTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;

    private static byte[] Constraint(FakePlayer by)
        => ModuleProtocol.WriteModuleToClients(ModuleProtocol.ConstraintCreateTag, by.SmallId,
            ConstraintPayloads.Build(by.SmallId, new EntityEnd(by.SmallId, 5), new SceneEnd("/Level/Wall")));

    private static int ConstraintsTo(World world, FakePlayer player, int before)
        => world.Transport.SentTo(player.Connection)
            .Skip(before)
            .Count(sent => ModuleProtocol.TryReadHandlerTag(sent.Message) == ModuleProtocol.ConstraintCreateTag);

    [Fact]
    public void Constraints_over_the_per_second_cap_are_refused()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();
        int before = world.Transport.SentTo(kanza.Connection).Count;

        // The built-in cap is five a second.
        joel.SendMany(Enumerable.Range(0, 6).Select(_ => Constraint(joel)));

        Assert.Equal(5, ConstraintsTo(world, kanza, before));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Constraint by Joel denied: over the per-second rate cap");
    }

    [Fact]
    public void Constraint_spam_strikes_the_spawn_guard_and_purges_the_players_spawns()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, SpawnBurstLimit = 2 });
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();
        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);
        int before = world.Transport.SentTo(kanza.Connection).Count;

        joel.SendMany(Enumerable.Range(0, 3).Select(_ => Constraint(joel)));

        Assert.Equal(2, ConstraintsTo(world, kanza, before));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Spam guard: Joel spawned 2 items in 5s, strike 1, dropping the spawn");
        Assert.Null(world.Server.Entities.Get(300));
        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));
    }

    [Fact]
    public void Repeated_constraint_spam_kicks_the_player()
    {
        using var world = new World(new ServerConfig
        {
            CullOrphanedEntities = false,
            SpawnBurstLimit = 1,
            SpamStrikesBeforeKick = 1,
        });
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        joel.SendMany(Enumerable.Range(0, 2).Select(_ => Constraint(joel)));

        Assert.Null(world.Server.Players.GetByPlatformId(JoelId));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Kicked Joel: Kicked for making too many constraints too quickly");
    }
}
