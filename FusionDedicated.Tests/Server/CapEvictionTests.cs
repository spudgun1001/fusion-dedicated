using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// What happens when the world is full.
///
/// A busy server ran three hours and then nobody could spawn anything. Every
/// entity belonged to somebody still connected, so nothing counted as abandoned,
/// nothing could be evicted, and every spawn from every player was refused until
/// a restart.
/// </summary>
public class CapEvictionTests
{
    private static EntityRegistry Full(int count, byte owner = 1)
    {
        var registry = new EntityRegistry();

        for (int i = 0; i < count; i++)
        {
            var entity = registry.Register(
                (ushort)(EntityRegistry.FirstEntityId + i), "Pack.Spawnable.Mag", owner, 0, 0, 0);

            entity.LastUpdate = DateTime.UtcNow.AddMinutes(-count + i);
        }

        return registry;
    }

    [Fact]
    public void Ordinarily_only_abandoned_props_are_taken()
    {
        // A player's own work is not removed to make room for somebody else.
        var registry = Full(10);

        Assert.Empty(registry.EvictOldest(5));
        Assert.Equal(10, registry.Count);
    }

    [Fact]
    public void An_orphan_is_taken_first()
    {
        var registry = Full(10);
        registry.SetOwner(EntityRegistry.FirstEntityId, null);

        Assert.Equal(new ushort[] { EntityRegistry.FirstEntityId }, registry.EvictOldest(5));
    }

    [Fact]
    public void As_a_last_resort_the_oldest_goes_whoever_owns_it()
    {
        // The alternative is a world that stays full and refuses everybody.
        var registry = Full(10);

        var evicted = registry.EvictOldest(3, anyOwner: true);

        Assert.Equal(3, evicted.Count);
        Assert.Equal(7, registry.Count);
    }

    [Fact]
    public void The_least_recently_touched_go_first()
    {
        var registry = Full(10);

        var evicted = registry.EvictOldest(2, anyOwner: true);

        Assert.Equal(
            new ushort[] { EntityRegistry.FirstEntityId, (ushort)(EntityRegistry.FirstEntityId + 1) },
            evicted);
    }

    [Fact]
    public void A_persistent_prop_is_never_taken_even_as_a_last_resort()
    {
        // Somebody marked it to survive a restart. Losing it to make room for a
        // magazine would be the opposite of what that means.
        var registry = Full(3);
        registry.Get(EntityRegistry.FirstEntityId)!.Persistent = true;

        var evicted = registry.EvictOldest(10, anyOwner: true);

        Assert.DoesNotContain(EntityRegistry.FirstEntityId, evicted);
        Assert.NotNull(registry.Get(EntityRegistry.FirstEntityId));
    }

    [Fact]
    public void Freed_ids_come_back_around()
    {
        // Ids are recycled already: the allocator skips whatever is in use and
        // wraps at the top of the range. With 65280 to go round and a cap far
        // below that, an id is not handed out again for a very long time, which
        // is what stops a client mapping a new prop onto an old one.
        var registry = new EntityRegistry();

        ushort first = registry.AllocateId();
        registry.Register(first, "Pack.Spawnable.Thing", 1, 0, 0, 0);

        ushort second = registry.AllocateId();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void An_id_in_use_is_never_handed_out_twice()
    {
        var registry = new EntityRegistry();
        var seen = new HashSet<ushort>();

        for (int i = 0; i < 500; i++)
        {
            ushort id = registry.AllocateId();

            Assert.True(seen.Add(id), $"id {id} was handed out twice");
            registry.Register(id, "Pack.Spawnable.Thing", 1, 0, 0, 0);
        }
    }
}
