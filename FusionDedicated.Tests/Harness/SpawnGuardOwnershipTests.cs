using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A spam strike purges what the player spawned. It used to take everything they
/// owned that was not inherited, which included kept doors and payphones they had
/// adopted and crates a shop plugin made for them.
/// </summary>
public class SpawnGuardOwnershipTests
{
    private const ulong JoelId = 76561198000000001;
    private const string Payphone = "spudgun1001.Payphone.Spawnable.PayphoneWall";

    /// <summary>A world where owning two entities is the limit, so a third spawn request strikes.</summary>
    private static World LimitOfTwo()
        => new(new ServerConfig { CullOrphanedEntities = false, MaxEntitiesPerPlayer = 2 });

    /// <summary>A kept prop nobody owns yet, waiting for a join to adopt it.</summary>
    private static void KeptProp(World world, ushort id)
    {
        var kept = world.Server.Entities.Register(id, Payphone, 0, 1f, 2f, 3f);
        kept.Persistent = true;
        kept.KeptAt = (1f, 2f, 3f);
        world.Server.Entities.SetOwner(id, null);
    }

    private static void AskToSpawn(FakePlayer player, string barcode)
        => player.Send(FusionProtocol.BuildSpawnRequest(player.SmallId, barcode, new Vec3(0, 0, 0), 1));

    [Fact]
    public void A_kept_prop_adopted_by_a_player_survives_a_spawn_guard_purge_of_that_player()
    {
        using var world = LimitOfTwo();
        KeptProp(world, 900);

        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        Assert.Equal((byte?)joel.SmallId, world.Server.Entities.Get(900)!.OwnerSmallId);

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 0, 0, 0);
        world.Spawn(joel, 301, "Pack.Spawnable.Crate", 0, 0, 0);
        AskToSpawn(joel, "Pack.Spawnable.Crate");

        Assert.Null(world.Server.Entities.Get(300));
        Assert.NotNull(world.Server.Entities.Get(900));
    }

    [Fact]
    public void Items_spawned_by_plugins_survive_a_spawn_guard_purge()
    {
        using var world = LimitOfTwo();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        ushort held = world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), JoelId);
        ushort crate = world.Server.SpawnForPlugin("Pack.Spawnable.Crate", 0f, 0f, 0f, Array.Empty<byte>());
        world.Sync();

        Assert.Equal((byte?)joel.SmallId, world.Server.Entities.Get(crate)!.OwnerSmallId);

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 0, 0, 0);
        world.Spawn(joel, 301, "Pack.Spawnable.Crate", 0, 0, 0);
        AskToSpawn(joel, "Pack.Spawnable.Crate");

        Assert.Null(world.Server.Entities.Get(300));
        Assert.NotNull(world.Server.Entities.Get(held));
        Assert.NotNull(world.Server.Entities.Get(crate));
    }

    [Fact]
    public void A_players_own_spawns_are_still_purged()
    {
        using var world = LimitOfTwo();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 0, 0, 0);
        world.Spawn(joel, 301, "Pack.Spawnable.Crate", 0, 0, 0);
        AskToSpawn(joel, "Pack.Spawnable.Crate");

        Assert.Null(world.Server.Entities.Get(300));
        Assert.Null(world.Server.Entities.Get(301));
        Assert.Contains(world.Server.RecentLog(2000), e => e.Message == "Removed 2 entities spawned by Joel");
    }

    [Fact]
    public void Plugin_spawns_and_kept_props_do_not_count_toward_the_spawn_limit()
    {
        using var world = LimitOfTwo();
        KeptProp(world, 900);

        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        world.Server.SpawnForPlayer("Pack.Spawnable.Pistol", 0f, 1f, 0f, Array.Empty<byte>(), JoelId);
        world.Server.SpawnForPlugin("Pack.Spawnable.Crate", 0f, 0f, 0f, Array.Empty<byte>());
        world.Sync();

        AskToSpawn(joel, "Pack.Spawnable.Barrel");

        Assert.Contains(world.Server.Entities.Entities, e => e.Barcode == "Pack.Spawnable.Barrel");
        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.StartsWith("Spam guard:"));
    }
}
