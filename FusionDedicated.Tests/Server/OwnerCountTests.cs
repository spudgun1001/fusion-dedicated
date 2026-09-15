using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Only what a player spawned themselves counts against them. Kept props, level props,
/// constraint ends, inherited props and plugin spawns were counted and purged with
/// their own spawns, so a spam strike deleted kept doors and shop props.
/// </summary>
public class OwnerCountTests
{
    private static TrackedEntity Crate() => new() { Id = 300, Barcode = "Pack.Spawnable.Crate", OwnerSmallId = 1 };

    [Fact]
    public void A_players_own_spawn_counts_against_them()
        => Assert.True(EntityRegistry.CountsAgainstOwner(Crate()));

    [Theory]
    [InlineData("Persistent")]
    [InlineData("Discovered")]
    [InlineData("Synthetic")]
    [InlineData("Inherited")]
    [InlineData("PluginSpawned")]
    public void An_entity_with_this_flag_does_not_count_against_its_owner(string flag)
    {
        var entity = Crate();

        switch (flag)
        {
            case "Persistent":
                entity.Persistent = true;
                break;
            case "Discovered":
                entity.Discovered = true;
                break;
            case "Synthetic":
                entity.Synthetic = true;
                break;
            case "Inherited":
                entity.Inherited = true;
                break;
            case "PluginSpawned":
                entity.PluginSpawned = true;
                break;
        }

        Assert.False(EntityRegistry.CountsAgainstOwner(entity));
    }

    [Fact]
    public void Only_a_players_own_spawns_are_counted_as_theirs()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Crate", 1, 0f, 0f, 0f);
        registry.Register(301, "Pack.Spawnable.Crate", 1, 0f, 0f, 0f).Persistent = true;
        registry.Register(302, "Pack.Spawnable.Crate", 1, 0f, 0f, 0f).Inherited = true;
        registry.Register(303, "Pack.Spawnable.Crate", 1, 0f, 0f, 0f).PluginSpawned = true;
        registry.Register(304, "Constraint.End", 1, 0f, 0f, 0f).Synthetic = true;
        registry.NotePose(500, 1, 0f, 0f, 0f);
        registry.Register(305, "Pack.Spawnable.Crate", 2, 0f, 0f, 0f);

        Assert.Equal(1, registry.SpawnsOwnedBy(1));
        Assert.Equal(1, registry.SpawnsOwnedBy(2));
    }

    [Fact]
    public void Discovered_entities_are_counted_per_owner()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Crate", 1, 0f, 0f, 0f);
        registry.NotePose(500, 1, 0f, 0f, 0f);
        registry.NotePose(501, 1, 0f, 0f, 0f);
        registry.NotePose(502, 2, 0f, 0f, 0f);

        Assert.Equal(2, registry.DiscoveredOwnedBy(1));
        Assert.Equal(1, registry.DiscoveredOwnedBy(2));
    }

    [Fact]
    public void Poses_for_unknown_ids_stop_registering_at_the_owners_cap()
    {
        var registry = new EntityRegistry();

        Assert.Equal(PoseNoted.Discovered, registry.NotePose(500, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2));
        Assert.Equal(PoseNoted.Discovered, registry.NotePose(501, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2));
        Assert.Equal(PoseNoted.OwnerAtCap, registry.NotePose(502, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2));

        Assert.Null(registry.Get(502));
        Assert.Equal(2, registry.DiscoveredOwnedBy(7));
    }

    [Fact]
    public void Another_owner_still_registers_when_one_is_at_the_cap()
    {
        var registry = new EntityRegistry();
        registry.NotePose(500, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2);
        registry.NotePose(501, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2);

        Assert.Equal(PoseNoted.Discovered, registry.NotePose(502, 8, 0f, 0f, 0f, maxDiscoveredPerOwner: 2));
        Assert.Equal((byte?)8, registry.Get(502)!.OwnerSmallId);
    }

    [Fact]
    public void A_known_entity_still_moves_when_its_owner_is_at_the_cap()
    {
        var registry = new EntityRegistry();
        registry.NotePose(500, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2);
        registry.NotePose(501, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2);

        Assert.Equal(PoseNoted.Updated, registry.NotePose(500, 7, 4f, 0f, 0f, maxDiscoveredPerOwner: 2));
        Assert.Equal(4f, registry.Get(500)!.X);
    }

    [Fact]
    public void A_cap_of_zero_registers_every_unknown_id()
    {
        var registry = new EntityRegistry();

        for (ushort id = 500; id < 510; id++)
        {
            Assert.Equal(PoseNoted.Discovered, registry.NotePose(id, 7, 0f, 0f, 0f));
        }

        Assert.Equal(10, registry.DiscoveredOwnedBy(7));
    }

    [Fact]
    public void Spawned_entities_do_not_use_up_the_discovered_cap()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Crate", 7, 0f, 0f, 0f);
        registry.Register(301, "Pack.Spawnable.Crate", 7, 0f, 0f, 0f);

        Assert.Equal(PoseNoted.Discovered, registry.NotePose(500, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 2));
    }

    [Fact]
    public void A_rig_id_or_a_full_world_is_ignored_rather_than_refused_for_the_cap()
    {
        var registry = new EntityRegistry { Capacity = 1 };

        Assert.Equal(PoseNoted.Ignored, registry.NotePose(100, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 5));

        registry.NotePose(500, 7, 0f, 0f, 0f, maxDiscoveredPerOwner: 5);
        registry.NotePose(501, 8, 0f, 0f, 0f, maxDiscoveredPerOwner: 5);

        Assert.Equal(PoseNoted.Ignored, registry.NotePose(502, 9, 0f, 0f, 0f, maxDiscoveredPerOwner: 5));
        Assert.Null(registry.Get(502));
    }
}
