using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A duplication mod copies a holstered item by asking for the same barcode again
/// with EntitySource None. The game sends None for loot drops and slot fills too, and
/// those can name something the player carries, so one match is allowed and a repeat
/// inside the window is refused.
/// </summary>
public class HolsterDuplicateTests
{
    private const ulong JoelId = 76561198000000001;
    private const string Pistol = "Pack.Spawnable.Pistol";
    private const string Loot = "Pack.Spawnable.Ball";
    private const ushort Held = 300;

    private static ServerConfig Plain() => new() { CullOrphanedEntities = false };

    private static FakePlayer Loaded(World world)
    {
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        return joel;
    }

    /// <summary>Puts a gun the player spawned into their slot, the way their game does.</summary>
    private static void Holster(World world, FakePlayer player, string barcode = Pistol,
        ushort id = Held, byte index = 1)
    {
        world.Spawn(player, id, barcode, 0, 1, 0);
        player.Send(ClientMessages.SlotInsert(player.SmallId, player.SmallId, id, index));
        world.Sync();
    }

    /// <summary>Takes it back out again, which is the grab the cheat rides on.</summary>
    private static void Draw(World world, FakePlayer player, byte index = 1)
    {
        player.Send(ClientMessages.SlotDrop(player.SmallId, player.SmallId, player.SmallId, index, 0));
        world.Sync();
    }

    private static void AskToSpawn(FakePlayer player, string barcode, byte source = FusionProtocol.SourceNone)
        => player.Send(FusionProtocol.BuildSpawnRequest(
            player.SmallId, barcode, new Vec3(0, 0, 0), 1, spawnEffect: false, source: source));

    private static int Copies(World world, string barcode)
        => world.Server.Entities.Entities.Count(e => e.Barcode == barcode);

    private static bool Struck(World world)
        => world.Server.RecentLog(2000).Any(e => e.Message.StartsWith("Spam guard:"));

    private static bool Refused(World world)
        => world.Server.RecentLog(2000).Any(e => e.Message.Contains("holster slot"));

    [Fact]
    public void The_first_spawn_of_a_holstered_barcode_is_allowed_and_noted()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol);

        Assert.Equal(2, Copies(world, Pistol));
        Assert.False(Struck(world));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Holster: Joel spawned '{Pistol}' while their slot 1 holds one, " +
                              "allowed as a first match");
    }

    [Fact]
    public void A_second_spawn_inside_the_window_is_refused_and_strikes()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.Equal(2, Copies(world, Pistol));
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Level == "WARN"
                 && e.Message == $"Spawn of '{Pistol}' by Joel denied: attempt 2 to respawn what is " +
                                 "in their holster slot 1");
        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Spam guard: Joel asked to spawn '{Pistol}' from their holster slot 1, " +
                              "attempt 2, strike 1, dropping the spawn");
    }

    [Fact]
    public void A_second_spawn_after_the_window_is_allowed_again()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol);
        world.Advance(TimeSpan.FromSeconds(11));
        AskToSpawn(joel, Pistol);

        Assert.Equal(3, Copies(world, Pistol));
        Assert.False(Struck(world));
    }

    [Fact]
    public void A_refusal_takes_nothing_the_player_owns()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);
        world.Spawn(joel, 400, "Pack.Spawnable.Crate", 5, 0, 5);

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.NotNull(world.Server.Entities.Get(Held));
        Assert.NotNull(world.Server.Entities.Get(400));
        Assert.DoesNotContain(world.Server.RecentLog(2000), e => e.Message.StartsWith("Removed "));
    }

    [Fact]
    public void An_item_just_drawn_is_still_protected_after_the_slot_record_has_gone()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);
        Draw(world, joel);

        Assert.Empty(world.Server.HolsteredBy(JoelId));

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Spawn of '{Pistol}' by Joel denied: attempt 2 to respawn what is " +
                              "in their holster slot 1");
    }

    [Fact]
    public void A_drawn_item_is_forgotten_once_the_memory_runs_out()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);
        Draw(world, joel);
        world.Advance(TimeSpan.FromSeconds(4));

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.False(Struck(world));
        Assert.False(Refused(world));
    }

    [Fact]
    public void Holstering_it_again_forgets_the_draw()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);
        Draw(world, joel);
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, Held, 1));
        world.Sync();

        // The slot record goes with the entity, so only the drawn memory could match now.
        world.Server.Entities.Remove(Held);

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.False(Struck(world));
        Assert.False(Refused(world));
    }

    [Fact]
    public void A_loot_drop_of_something_they_do_not_carry_is_allowed()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Loot);
        AskToSpawn(joel, Loot);

        Assert.Equal(2, Copies(world, Loot));
        Assert.False(Struck(world));
    }

    [Fact]
    public void The_same_barcode_from_a_real_spawn_menu_is_never_refused()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);

        AskToSpawn(joel, Pistol, FusionProtocol.SourcePlayer);
        AskToSpawn(joel, Pistol, FusionProtocol.SourcePlayer);

        Assert.Equal(3, Copies(world, Pistol));
        Assert.False(Struck(world));
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

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.NotNull(world.Server.Players.GetByPlatformId(JoelId));

        // The guard counts one strike per window, off the wall clock rather than the
        // world's, so the next try has to be a real second later.
        Thread.Sleep(1100);
        AskToSpawn(joel, Pistol);

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

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.Equal(3, Copies(world, Pistol));
        Assert.False(Struck(world));
    }

    [Fact]
    public void An_owner_is_treated_the_same_way()
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

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.Contains(world.Server.RecentLog(2000),
            e => e.Message == $"Spawn of '{Pistol}' by Joel denied: attempt 2 to respawn what is " +
                              "in their holster slot 1");
    }

    [Fact]
    public void A_slot_holding_an_entity_the_server_never_knew_matches_nothing()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);

        // A client can name an id the registry never registered, which leaves the
        // server with no barcode to compare against.
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, 9999, 1));
        world.Sync();

        AskToSpawn(joel, Pistol);
        AskToSpawn(joel, Pistol);

        Assert.False(Struck(world));
        Assert.False(Refused(world));
    }

    [Fact]
    public void An_empty_barcode_matches_nothing()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);

        // A prop the server only learned about by pose carries no barcode, and a
        // truncated spawn request asks for none.
        Holster(world, joel, barcode: "", id: 310);

        AskToSpawn(joel, "");
        AskToSpawn(joel, "");

        Assert.False(Struck(world));
        Assert.False(Refused(world));
    }

    [Fact]
    public void A_player_who_leaves_takes_their_draw_with_them()
    {
        using var world = new World(Plain());
        var joel = Loaded(world);
        Holster(world, joel);
        Draw(world, joel);

        byte smallId = joel.SmallId;
        world.Leave(joel, "Closing Connection");

        var newbie = world.Join(76561198000000009, "Newbie");
        newbie.FinishLoading();

        Assert.Equal(smallId, newbie.SmallId);

        AskToSpawn(newbie, Pistol);
        AskToSpawn(newbie, Pistol);

        Assert.False(Struck(world));
        Assert.False(Refused(world));
    }
}
