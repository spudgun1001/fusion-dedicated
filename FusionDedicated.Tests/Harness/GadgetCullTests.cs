using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Nimbus guns and spawn guns piled up, about 250 of each a session, because their
/// owners stayed online and a connected player's props are never idle-culled.
/// </summary>
public class GadgetCullTests
{
    private const string Nimbus = "c1534c5a-6b38-438a-a324-d7e147616467";
    private const string SpawnGun = "c1534c5a-5747-42a2-bd08-ab3b47616467";
    private const string Constrainer = "c1534c5a-3813-49d6-a98c-f595436f6e73";

    private static (World World, FakePlayer Kanza) Build()
    {
        var world = new World(new ServerConfig { CullOrphanedEntities = true });
        var kanza = world.Join(76561198000000001, "Kanza");
        kanza.FinishLoading();
        return (world, kanza);
    }

    private static void Wait(World world, int seconds)
    {
        world.Advance(TimeSpan.FromSeconds(seconds));
        world.Tick();
    }

    [Fact]
    public void Defaults_to_three_minutes()
        => Assert.Equal(180, new ServerConfig().GadgetTimeoutSeconds);

    [Fact]
    public void Idle_nimbus_and_spawn_guns_go_after_three_minutes()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Nimbus, 0, 0, 0);
        world.Spawn(kanza, 301, SpawnGun, 0, 0, 0);
        world.Spawn(kanza, 302, "Pack.Spawnable.Crate", 0, 0, 0);

        Wait(world, 170);
        Assert.NotNull(world.Server.Entities.Get(300));

        Wait(world, 11);
        Assert.Null(world.Server.Entities.Get(300));
        Assert.Null(world.Server.Entities.Get(301));
        Assert.NotNull(world.Server.Entities.Get(302));
    }

    [Fact]
    public void A_held_gadget_stays()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Nimbus, 0, 0, 0);
        kanza.Grab(300);
        Wait(world, 600);

        Assert.NotNull(world.Server.Entities.Get(300));
    }

    [Fact]
    public void A_holstered_gadget_stays()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, SpawnGun, 0, 0, 0);
        kanza.Send(ClientMessages.SlotInsert(kanza.SmallId, kanza.SmallId, 300, 1));
        Wait(world, 600);

        Assert.NotNull(world.Server.Entities.Get(300));
    }

    [Fact]
    public void A_constrainer_is_left_alone()
    {
        // Welds hang off it.
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Constrainer, 0, 0, 0);
        Wait(world, 600);

        Assert.NotNull(world.Server.Entities.Get(300));
    }

    [Fact]
    public void Zero_turns_it_off()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = true, GadgetTimeoutSeconds = 0 });
        var kanza = world.Join(76561198000000001, "Kanza");
        kanza.FinishLoading();

        world.Spawn(kanza, 300, Nimbus, 0, 0, 0);
        Wait(world, 600);

        Assert.NotNull(world.Server.Entities.Get(300));
    }

    [Fact]
    public void The_minute_log_counts_culled_magazines_gadgets_and_used_shells()
    {
        var (world, kanza) = Build();
        using var _ = world;

        world.Spawn(kanza, 300, Nimbus, 0, 0, 0);
        world.Spawn(kanza, 301, "Rexmeck.WeaponPackLT.Spawnable.Magglock17gen5", 0, 0, 0);
        world.Tick();

        world.Spawn(kanza, 302, "Pack.Spawnable.Mossberg", 0, 0, 0);
        world.Spawn(kanza, 303, "Pack.Spawnable.Mag12Gauge", 0, 0, 0);
        world.Spawn(kanza, 304, "Pack.Spawnable.Mag12Gauge", 0, 0, 0);
        kanza.Send(ClientMessages.MagazineInsert(kanza.SmallId, 303, 302));
        kanza.Send(ClientMessages.MagazineInsert(kanza.SmallId, 304, 302));
        Wait(world, 181);
        Wait(world, 60);

        // The shells are counted in the minute they were loaded, before the clocks run out.
        Assert.Equal(new[]
            {
                "Culled in the last minute: 0 magazine(s), 0 gadget(s), 2 used shell(s)",
                "Culled in the last minute: 1 magazine(s), 1 gadget(s), 0 used shell(s)",
            },
            world.Server.RecentLog(2000).Select(e => e.Message).Where(m => m.StartsWith("Culled in the last minute")));
    }
}
