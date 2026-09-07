using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The forced eviction added to stop a full world locking spawning turned out to
/// be a way to destroy other people's builds on demand.
///
/// A pose update for an id nobody spawned registers a new entity, from an
/// unauthenticated packet, with no rate limit. So anybody could fill the world
/// with entities of their own, send one ordinary spawn, and have the server take
/// the thirty-two least recently touched things it knew about. Those were, by
/// construction, everybody else's real props.
/// </summary>
public class EvictionSafetyTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static EntityRegistry World()
    {
        var registry = new EntityRegistry { Capacity = 100 };

        // Somebody's build, sitting still because it is finished.
        for (int i = 0; i < 5; i++)
        {
            var built = registry.Register((ushort)(300 + i), "Pack.Spawnable.Wall", 1, 0, 0, 0);
            built.LastUpdate = Now.AddHours(-1);
        }

        return registry;
    }

    [Fact]
    public void Something_being_used_right_now_is_never_taken()
    {
        var registry = World();
        var held = registry.Register(400, "Pack.Spawnable.Gun", 2, 0, 0, 0);
        held.LastUpdate = Now;

        var evicted = registry.EvictOldest(32, anyOwner: true, idleFor: TimeSpan.FromMinutes(2));

        Assert.DoesNotContain(evicted, id => id == 400);
    }

    [Fact]
    public void Filling_the_world_with_fresh_entities_evicts_none_of_them_and_none_of_yours()
    {
        // The exploit, as it was: the attacker's entities are the newest so they
        // sort last, and the oldest real props went instead. Now nothing recent
        // is eligible at all, so a burst of new entities frees nothing and the
        // spawn is refused rather than somebody's work being destroyed.
        var registry = World();

        for (int i = 0; i < 50; i++)
        {
            registry.NotePose((ushort)(1000 + i), 9, 0, 0, 0);
        }

        var evicted = registry.EvictOldest(32, anyOwner: true, idleFor: TimeSpan.FromMinutes(2));

        Assert.DoesNotContain(evicted, id => id >= 1000);
    }

    [Fact]
    public void A_client_cannot_inflate_the_world_without_end()
    {
        // Poses arrive from an unauthenticated packet with no rate limit, so this
        // is the amplification that made the eviction worth aiming.
        //
        // The ceiling is twice the spawn cap, not equal to it: a scene object is
        // not somebody's spawn and no longer counts against what people may
        // spawn, so the registry has to hold both without one starving the other.
        var registry = new EntityRegistry { Capacity = 20 };

        for (int i = 0; i < 500; i++)
        {
            registry.NotePose((ushort)(1000 + i), 9, 0, 0, 0);
        }

        Assert.Equal(40, registry.Count);
    }

    [Fact]
    public void A_level_full_of_scene_objects_does_not_stop_anybody_spawning()
    {
        // Scene objects are never reclaimed, so counting them against the spawn
        // cap meant a busy level filled it with its own furniture and refused
        // every spawn for the rest of the session.
        var registry = new EntityRegistry { Capacity = 100 };

        for (int i = 0; i < 90; i++)
        {
            registry.NotePose((ushort)(1000 + i), 9, 0, 0, 0);
        }

        registry.Register(300, "Pack.Spawnable.Crate", 1, 0, 0, 0);

        Assert.Equal(91, registry.Count);
        Assert.Equal(1, registry.SpawnedCount);
    }

    [Fact]
    public void No_cap_means_no_limit_so_nothing_changes_for_a_server_that_set_none()
    {
        var registry = new EntityRegistry { Capacity = 0 };

        for (int i = 0; i < 300; i++)
        {
            registry.NotePose((ushort)(1000 + i), 9, 0, 0, 0);
        }

        Assert.Equal(300, registry.Count);
    }

    [Fact]
    public void A_pose_for_something_already_known_is_still_recorded_at_the_cap()
    {
        // Refusing these would freeze every prop in place once the world is full.
        var registry = new EntityRegistry { Capacity = 1 };
        registry.NotePose(500, 1, 0, 0, 0);

        registry.NotePose(500, 1, 7, 8, 9);

        Assert.Equal(7, registry.Get(500)!.X);
    }

    [Fact]
    public void A_prop_that_came_with_the_level_is_never_forced_out()
    {
        // Despawning one desynchronises every client's copy of the scene.
        var registry = World();
        var scene = registry.Register(500, "SLZ.BONELAB.Content.Spawnable.Chair", 1, 0, 0, 0);
        scene.Discovered = true;
        scene.LastUpdate = Now.AddHours(-2);

        Assert.DoesNotContain(registry.EvictOldest(32, anyOwner: true), id => id == 500);
    }

    [Fact]
    public void A_constraint_end_is_never_forced_out()
    {
        // Clients only know it from the constraint message, so a despawn for it
        // names something they cannot act on, and it would leave the other end.
        var registry = World();
        var end = registry.Register(501, "fusion.constraint", 1, 0, 0, 0);
        end.Synthetic = true;
        end.LastUpdate = Now.AddHours(-2);

        Assert.DoesNotContain(registry.EvictOldest(32, anyOwner: true), id => id == 501);
    }

    [Fact]
    public void A_prop_marked_to_survive_a_restart_is_never_forced_out()
    {
        var registry = World();
        var kept = registry.Register(502, "DayTrip.PortableBodymall.Spawnable.Bodymall", 1, 0, 0, 0);
        kept.Persistent = true;
        kept.LastUpdate = Now.AddDays(-1);

        Assert.DoesNotContain(registry.EvictOldest(32, anyOwner: true), id => id == 502);
    }

    [Fact]
    public void Something_genuinely_abandoned_is_still_taken()
    {
        // The point of the whole thing: a full world must not stay full.
        var registry = World();

        var evicted = registry.EvictOldest(32, anyOwner: true, idleFor: TimeSpan.FromMinutes(2));

        Assert.Equal(5, evicted.Count);
    }

    [Fact]
    public void The_ordinary_pass_is_unchanged()
    {
        // Without anyOwner it still only takes inherited or ownerless props, at
        // any age, exactly as before.
        var registry = World();
        registry.SetOwner(300, null);

        Assert.Equal(new ushort[] { 300 }, registry.EvictOldest(32));
    }
}
