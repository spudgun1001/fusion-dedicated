using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Culling only ever removed props whose owner had left, or props handed on when
/// somebody left. A magazine dropped by a player who is still connected is
/// neither, so nothing removed it and a session's worth of them piled up.
/// </summary>
public class IdleEntityCullTests
{
    private static readonly TimeSpan Orphan = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan Inherited = TimeSpan.FromSeconds(900);
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(300);

    /// <summary>Registers a prop owned by a connected player and ages it.</summary>
    private static EntityRegistry WithDroppedProp(ushort id, int ageSeconds)
    {
        var registry = new EntityRegistry();
        registry.Register(id, "Test.Magazine", owner: 3, 1f, 2f, 3f);
        registry.Get(id)!.LastUpdate = DateTime.UtcNow.AddSeconds(-ageSeconds);
        return registry;
    }

    [Fact]
    public void A_prop_left_by_a_connected_player_used_to_stay_for_ever()
    {
        var registry = WithDroppedProp(300, 3600);

        // No idle timeout is the old behaviour, kept for anyone who wants it.
        Assert.Empty(registry.CullStale(Orphan, Inherited));
        Assert.NotNull(registry.Get(300));
    }

    [Fact]
    public void An_idle_timeout_removes_a_prop_its_owner_walked_away_from()
    {
        var registry = WithDroppedProp(301, 3600);

        Assert.Equal(new ushort[] { 301 }, registry.CullStale(Orphan, Inherited, Idle));
        Assert.Null(registry.Get(301));
    }

    [Fact]
    public void A_prop_that_moved_recently_is_left_alone()
    {
        var registry = WithDroppedProp(302, 10);

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle));
        Assert.NotNull(registry.Get(302));
    }

    [Fact]
    public void A_scene_prop_somebody_picked_up_is_never_culled_for_being_idle()
    {
        // Discovered entities have no barcode and may be part of the level itself.
        // Despawning one desynchronises every client.
        var registry = new EntityRegistry();
        registry.NotePose(303, 3, 1f, 2f, 3f);
        registry.Get(303)!.LastUpdate = DateTime.UtcNow.AddSeconds(-3600);

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle));
        Assert.NotNull(registry.Get(303));
    }

    [Fact]
    public void An_orphan_is_still_culled_on_its_own_shorter_clock()
    {
        // Orphaning refreshes the clock, so age it after rather than before.
        var registry = WithDroppedProp(304, 0);
        registry.SetOwner(304, null);
        registry.Get(304)!.LastUpdate = DateTime.UtcNow.AddSeconds(-200);

        Assert.Equal(new ushort[] { 304 }, registry.CullStale(Orphan, Inherited, Idle));
    }

    [Fact]
    public void An_inherited_prop_is_still_culled_on_its_own_longer_clock()
    {
        var registry = WithDroppedProp(305, 1000);
        registry.Get(305)!.Inherited = true;

        Assert.Equal(new ushort[] { 305 }, registry.CullStale(Orphan, Inherited, Idle));
    }

    [Fact]
    public void A_zero_idle_timeout_means_never_rather_than_immediately()
    {
        var registry = WithDroppedProp(306, 3600);

        Assert.Empty(registry.CullStale(Orphan, Inherited, TimeSpan.Zero));
        Assert.NotNull(registry.Get(306));
    }

    [Fact]
    public void A_vehicle_somebody_is_sitting_in_is_never_culled()
    {
        // A parked car sleeps with its driver in it and sends no poses, so every
        // clock here saw it as abandoned. Inherited is the usual case: its owner
        // left while somebody was still sitting in it.
        var registry = new EntityRegistry();
        registry.Register(307, "Pack.Spawnable.Atv", owner: 3, 1f, 2f, 3f);

        var vehicle = registry.Get(307)!;
        vehicle.Inherited = true;
        vehicle.LastUpdate = DateTime.UtcNow.AddSeconds(-3600);

        registry.SetOccupied(307, true);

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle));
        Assert.NotNull(registry.Get(307));
    }

    [Fact]
    public void Getting_out_starts_its_clock_again()
    {
        // Not culled the moment the last rider steps out, however long it was parked.
        var registry = new EntityRegistry();
        registry.Register(308, "Pack.Spawnable.Atv", owner: 3, 1f, 2f, 3f);
        registry.Get(308)!.Inherited = true;
        registry.Get(308)!.LastUpdate = DateTime.UtcNow.AddSeconds(-3600);
        registry.SetOccupied(308, true);

        registry.SetOccupied(308, false);

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle));

        registry.Get(308)!.LastUpdate = DateTime.UtcNow.AddSeconds(-3600);

        Assert.Equal(new ushort[] { 308 }, registry.CullStale(Orphan, Inherited, Idle));
    }
}
