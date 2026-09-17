using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A spawn carrying a source the game itself never sends is a duplication cheat:
/// a mod that respawns a holstered item asks for source None. Those are refused
/// at every rank and strike the spawn guard.
/// </summary>
public class SpawnSourceGateTests
{
    private const ulong JoelId = 76561198000000001;
    private const string Crate = "Pack.Spawnable.Crate";

    private static ServerConfig Plain() => new() { CullOrphanedEntities = false };

    private static FakePlayer Loaded(World world)
    {
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        return joel;
    }

    private static void AskToSpawn(FakePlayer player, byte source, string barcode = Crate)
        => player.Send(FusionProtocol.BuildSpawnRequest(
            player.SmallId, barcode, new Vec3(0, 0, 0), 1, spawnEffect: false, source: source));

    private static bool Spawned(World world, string barcode = Crate)
        => world.Server.Entities.Entities.Any(e => e.Barcode == barcode);

    [Fact]
    public void A_spawn_with_source_none_is_refused_and_nothing_is_created()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        int before = world.Server.Entities.SpawnedCount;

        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.False(Spawned(world));
        Assert.Equal(before, world.Server.Entities.SpawnedCount);
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN"
                 && e.Message == $"Spawn of '{Crate}' by Joel denied: spawn source 0 is not allowed");
    }

    [Fact]
    public void A_refused_source_strikes_the_spawn_guard_without_kicking_on_the_first_try()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);

        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Spam guard: Joel asked to spawn '{Crate}' with source 0, " +
                              "strike 1, dropping the spawn");
        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));
    }

    [Theory]
    [InlineData(FusionProtocol.SourceScene)]
    [InlineData(FusionProtocol.SourcePlayer)]
    public void The_sources_the_game_uses_still_spawn(byte source)
    {
        using var world = new World(Plain());
        var joel = Loaded(world);

        AskToSpawn(joel, source);

        Assert.True(Spawned(world));
        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.Contains("is not allowed"));
    }

    [Fact]
    public void Repeated_refused_sources_reach_the_strike_limit_and_kick()
    {
        using var world = new World(new ServerConfig
        {
            CullOrphanedEntities = false,
            SpawnWindowSeconds = 1,
            SpamStrikesBeforeKick = 2,
        });
        var joel = Loaded(world);

        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));

        // The guard counts one strike per window, off the wall clock rather than the
        // world's, so the second try has to be a real second later.
        Thread.Sleep(1100);
        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.Null(world.Server.Players.GetByPlatformId(JoelId));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Kicked Joel: Kicked for spawning with a forged source");
    }

    [Fact]
    public void A_strike_purges_what_the_player_spawned()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        world.Spawn(joel, 300, Crate, 1, 2, 3);

        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.Null(world.Server.Entities.Get(300));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Removed 1 entities spawned by Joel");
    }

    [Fact]
    public void An_empty_list_allows_any_source()
    {
        var config = Plain();
        config.AllowedSpawnSources.Clear();

        using var world = new World(config);
        var joel = Loaded(world);

        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.True(Spawned(world));
    }

    [Fact]
    public void An_owner_is_refused_the_same_way()
    {
        var config = Plain();
        config.Permissions.Add(new PermissionEntry
        {
            PlatformId = JoelId,
            Username = "Joel",
            Level = PermissionLevel.Owner,
        });

        using var world = new World(config);
        var joel = Loaded(world);

        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.False(Spawned(world));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Spawn of '{Crate}' by Joel denied: spawn source 0 is not allowed");
    }

    [Fact]
    public void An_exempt_source_still_passes_the_rank_gate()
    {
        var config = Plain();
        config.Spawning = PermissionLevel.Operator;
        config.SpawningExemptSources.Add(FusionProtocol.SourceScene);

        using var world = new World(config);
        var joel = Loaded(world);

        AskToSpawn(joel, FusionProtocol.SourceScene);

        Assert.True(Spawned(world));
    }

    [Fact]
    public void Exempting_a_source_does_not_let_it_past_the_source_gate()
    {
        var config = Plain();
        config.SpawningExemptSources.Add(FusionProtocol.SourceNone);

        using var world = new World(config);
        var joel = Loaded(world);

        AskToSpawn(joel, FusionProtocol.SourceNone);

        Assert.False(Spawned(world));
    }
}
