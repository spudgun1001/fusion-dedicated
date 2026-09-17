using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A duplication mod copies a holstered item by asking for the same barcode again
/// with EntitySource None and putting the copy back in the slot. Loot drops and
/// level-load slot fills carry None too, so only a barcode the player is already
/// holstering is refused.
/// </summary>
public class HolsterDuplicateTests
{
    private const ulong JoelId = 76561198000000001;
    private const string Pistol = "Pack.Spawnable.Pistol";
    private const string Loot = "Pack.Spawnable.Ball";

    private static ServerConfig Plain() => new() { CullOrphanedEntities = false };

    private static FakePlayer Loaded(World world)
    {
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        return joel;
    }

    /// <summary>
    /// Puts a gun in a player's slot the way their game does. Spawned for them rather
    /// than by them, so a spawn guard purge leaves it alone and a test can still see it.
    /// </summary>
    private static ushort Holster(World world, FakePlayer player, string barcode = Pistol, byte index = 1)
    {
        ushort id = world.Server.SpawnForPlayer(barcode, 0f, 1f, 0f, Array.Empty<byte>(), JoelId);
        world.Sync();

        player.Send(ClientMessages.SlotInsert(player.SmallId, player.SmallId, id, index));
        world.Sync();

        return id;
    }

    private static void AskToSpawn(FakePlayer player, string barcode, byte source)
        => player.Send(FusionProtocol.BuildSpawnRequest(
            player.SmallId, barcode, new Vec3(0, 0, 0), 1, spawnEffect: false, source: source));

    private static int Copies(World world, string barcode)
        => world.Server.Entities.Entities.Count(e => e.Barcode == barcode);

    private static bool Struck(World world)
        => world.Server.RecentLog(2000).Any(e => e.Message.StartsWith("Spam guard:"));

    [Fact]
    public void A_source_none_spawn_of_a_holstered_barcode_is_refused()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        Assert.Single(world.Server.HolsteredBy(JoelId));

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Equal(1, Copies(world, Pistol));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN"
                 && e.Message == $"Spawn of '{Pistol}' by Joel denied: it is already in their holster slot 1");
    }

    [Fact]
    public void The_refusal_strikes_the_spawn_guard()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Spam guard: Joel asked to spawn '{Pistol}', already in their " +
                              "holster slot 1, strike 1, dropping the spawn");
        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));
    }

    [Fact]
    public void The_same_barcode_from_a_real_spawn_menu_is_allowed()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol, FusionProtocol.SourcePlayer);

        Assert.Equal(2, Copies(world, Pistol));
        Assert.False(Struck(world));
    }

    [Fact]
    public void A_loot_drop_of_something_they_do_not_have_holstered_is_allowed()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Loot, FusionProtocol.SourceNone);

        Assert.Equal(1, Copies(world, Loot));
        Assert.False(Struck(world));
    }

    [Fact]
    public void An_empty_holster_refuses_nothing()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Equal(1, Copies(world, Pistol));
        Assert.False(Struck(world));
    }

    [Fact]
    public void An_item_they_have_put_down_again_is_no_longer_protected()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        ushort pistol = Holster(world, joel);

        joel.Send(ClientMessages.SlotDrop(joel.SmallId, joel.SmallId, joel.SmallId, 1, 0));
        world.Sync();

        Assert.Empty(world.Server.HolsteredBy(JoelId));

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Equal(2, Copies(world, Pistol));
        Assert.NotNull(world.Server.Entities.Get(pistol));
    }

    [Fact]
    public void Repeated_duplicates_reach_the_strike_limit_and_kick()
    {
        using var world = new World(new ServerConfig
        {
            CullOrphanedEntities = false,
            SpawnWindowSeconds = 1,
            SpamStrikesBeforeKick = 2,
        });
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));

        // The guard counts one strike per window, off the wall clock rather than the
        // world's, so the second try has to be a real second later.
        Thread.Sleep(1100);
        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Null(world.Server.Players.GetByPlatformId(JoelId));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == "Kicked Joel: Kicked for duplicating a holstered item");
    }

    [Fact]
    public void Nothing_is_refused_while_the_switch_is_off()
    {
        var config = Plain();
        config.BlockHolsterDuplicates = false;

        using var world = new World(config);
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Equal(2, Copies(world, Pistol));
        Assert.False(Struck(world));
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
        Holster(world, joel);

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Equal(1, Copies(world, Pistol));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Spawn of '{Pistol}' by Joel denied: it is already in their holster slot 1");
    }

    [Fact]
    public void A_slot_the_server_cannot_name_a_barcode_for_is_allowed_through()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        ushort pistol = Holster(world, joel);

        // The slot keeps the entity, not the barcode, so forgetting the entity leaves
        // the server with nothing to compare against.
        world.Server.Entities.Remove(pistol);

        AskToSpawn(joel, Pistol, FusionProtocol.SourceNone);

        Assert.Equal(1, Copies(world, Pistol));
        Assert.False(Struck(world));
    }
}
