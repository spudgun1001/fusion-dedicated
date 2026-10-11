using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Entities a client makes without a spawn request, picking up a scene prop, the
/// constrainer, are relayed but never registered, so they are invisible to the
/// panel and to cleanup. A pose update for an unknown id is them announcing
/// themselves.
/// </summary>
public class DiscoveredEntityTests
{
    private static EntityRegistry Registry() => new();

    [Fact]
    public void A_pose_for_an_unknown_id_registers_a_discovered_entity()
    {
        var registry = Registry();

        registry.NotePose(500, 7, 1f, 2f, 3f);

        var entity = registry.Get(500);

        Assert.NotNull(entity);
        Assert.True(entity!.Discovered);
        Assert.Equal((byte?)7, entity.OwnerSmallId);
    }

    [Fact]
    public void A_pose_below_the_first_entity_id_is_ignored_because_that_range_is_player_rigs()
    {
        var registry = Registry();

        registry.NotePose(100, 7, 0f, 0f, 0f);

        Assert.Null(registry.Get(100));
    }

    [Fact]
    public void A_pose_for_a_known_entity_updates_it_without_marking_it_discovered()
    {
        var registry = Registry();
        ushort id = registry.AllocateId();
        registry.Register(id, "A.B.C.D", 3, 0f, 0f, 0f);

        registry.NotePose(id, 3, 9f, 9f, 9f);

        var entity = registry.Get(id)!;

        Assert.False(entity.Discovered);
        Assert.Equal(9f, entity.X);
    }

    [Fact]
    public void Discovered_entities_are_excluded_from_stale_culling()
    {
        var registry = Registry();
        registry.NotePose(500, 7, 0f, 0f, 0f);

        var culled = registry.CullStale(TimeSpan.Zero, TimeSpan.Zero);

        Assert.DoesNotContain((ushort)500, culled);
        Assert.NotNull(registry.Get(500));
    }

    [Fact]
    public void Discovered_entities_are_excluded_from_orphan_culling()
    {
        var registry = Registry();
        registry.NotePose(500, 7, 0f, 0f, 0f);
        registry.SetOwner(500, null);

        Assert.DoesNotContain((ushort)500, registry.CullOrphans(TimeSpan.Zero));
    }

    // Owner, 2026-10-11: map objects such as doors are never cleared.
    [Fact]
    public void Clearing_never_removes_map_objects()
    {
        var registry = Registry();
        ushort spawned = registry.AllocateId();
        registry.Register(spawned, "A.B.C.D", 3, 0f, 0f, 0f);
        registry.NotePose(500, 7, 0f, 0f, 0f);

        var removed = registry.Clear();

        Assert.Contains(spawned, removed);
        Assert.DoesNotContain((ushort)500, removed);
        Assert.NotNull(registry.Get(500));
    }

    [Fact]
    public void The_panel_has_no_way_to_clear_map_objects()
    {
        string page = File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(typeof(FusionServer).Assembly.Location)!, "Web", "index.html"));

        Assert.DoesNotContain("includeDiscovered", Web.DashboardSource.Text());
        Assert.DoesNotContain("includeDiscovered", page);
    }

    [Fact]
    public void The_panel_will_not_remove_a_map_object_on_its_own()
    {
        string despawn = Web.DashboardSource.Method("private void HandleDespawn(");

        Assert.Contains("That is part of the map, so it stays.", despawn);
    }

    [Fact]
    public void Discovered_entities_are_counted_separately()
    {
        var registry = Registry();
        ushort spawned = registry.AllocateId();
        registry.Register(spawned, "A.B.C.D", 3, 0f, 0f, 0f);
        registry.NotePose(500, 7, 0f, 0f, 0f);
        registry.NotePose(501, 7, 0f, 0f, 0f);

        Assert.Equal(2, registry.DiscoveredCount);
        Assert.Equal(3, registry.Count);
    }

    [Fact]
    public void A_discovered_id_is_never_handed_out_by_the_allocator()
    {
        var registry = Registry();
        registry.NotePose(EntityRegistry.FirstEntityId, 7, 0f, 0f, 0f);

        Assert.NotEqual(EntityRegistry.FirstEntityId, registry.AllocateId());
    }
}
