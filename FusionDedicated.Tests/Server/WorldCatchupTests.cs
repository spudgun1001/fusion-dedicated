using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A player joining a running server used to see an empty level, because Fusion
/// only sends creation catch-up from the host and no client here is the host.
/// </summary>
public class WorldCatchupTests
{
    private static EntityRegistry WithSpawns()
    {
        var registry = new EntityRegistry();

        registry.Register(10, "Rexmeck.GLOCK17.Spawnable.GLOCK17", 1, 1, 2, 3);
        registry.Register(11, "SomePack.Spawnable.Crate", 2, 4, 5, 6);

        return registry;
    }

    [Fact]
    public void Everything_somebody_spawned_is_replayed()
    {
        var replay = WorldCatchup.For(WithSpawns().Entities);

        Assert.Equal(2, replay.Count);
    }

    [Fact]
    public void An_empty_world_replays_nothing()
        => Assert.Empty(WorldCatchup.For(new EntityRegistry().Entities));

    [Fact]
    public void A_prop_that_came_with_the_level_is_not_replayed()
    {
        // The newcomer loads the same level, so their own copy already has it.
        // Sending it would put a second one in the world.
        var registry = WithSpawns();
        registry.Register(12, "SLZ.BONELAB.Content.Spawnable.Chair", 1, 7, 8, 9);
        registry.Get(12)!.Discovered = true;

        Assert.DoesNotContain(WorldCatchup.For(registry.Entities), e => e.Id == 12);
    }

    [Fact]
    public void A_persistent_prop_is_replayed_like_anything_else()
    {
        var registry = WithSpawns();
        registry.Register(13, "DayTrip.PortableBodymall.Spawnable.Bodymall", 1, 1, 1, 1);
        registry.Get(13)!.Persistent = true;

        Assert.Contains(WorldCatchup.For(registry.Entities), e => e.Id == 13);
    }

    [Fact]
    public void An_entity_with_no_barcode_is_left_out()
    {
        // There is nothing to tell a client to spawn, so sending it would only
        // be a message they cannot act on.
        var registry = WithSpawns();
        registry.Register(14, "", 1, 0, 0, 0);

        Assert.DoesNotContain(WorldCatchup.For(registry.Entities), e => e.Id == 14);
    }

    [Fact]
    public void They_go_out_in_id_order()
    {
        var registry = new EntityRegistry();
        registry.Register(30, "Pack.Spawnable.Third", 1, 0, 0, 0);
        registry.Register(10, "Pack.Spawnable.First", 1, 0, 0, 0);
        registry.Register(20, "Pack.Spawnable.Second", 1, 0, 0, 0);

        Assert.Equal(new ushort[] { 10, 20, 30 },
            WorldCatchup.For(registry.Entities).Select(e => e.Id));
    }

    [Fact]
    public void An_orphan_is_still_replayed()
    {
        // Somebody left and their gun was handed on. It is still in the world,
        // so a newcomer has to be told about it.
        var registry = WithSpawns();
        registry.SetOwner(10, null);

        Assert.Contains(WorldCatchup.For(registry.Entities), e => e.Id == 10);
    }

    [Fact]
    public void The_rotation_goes_with_it()
    {
        var registry = new EntityRegistry();
        var rotation = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        registry.Register(10, "Pack.Spawnable.Thing", 1, 0, 0, 0, rotation);

        Assert.Equal(rotation, WorldCatchup.For(registry.Entities).Single().Rotation);
    }

    [Fact]
    public void The_ends_of_a_constraint_are_not_replayed_as_spawns()
    {
        // They are tracked so they count against the cap, but their barcode is
        // one the server invented. Telling a client to spawn it would name
        // something no pallet has, and it would do it twice per constraint.
        var registry = WithSpawns();
        registry.Register(20, "fusion.constraint", 1, 0, 0, 0).Synthetic = true;
        registry.Register(21, "fusion.constraint", 1, 0, 0, 0).Synthetic = true;

        var replay = WorldCatchup.For(registry.Entities);

        Assert.DoesNotContain(replay, e => e.Synthetic);
        Assert.Equal(2, replay.Count);
    }

    private static readonly Func<byte, bool> Here = id => id is 1 or 2 or 3;

    [Fact]
    public void A_scene_prop_names_whoever_owns_it_now()
    {
        // The newcomer asks that player what the prop is doing. The first person
        // to network it may have put it down long ago.
        Assert.Equal(2, WorldCatchup.PropOwner(current: 2, cached: 1, newcomer: 3, present: Here));
    }

    [Fact]
    public void The_first_owner_is_named_when_nobody_owns_it_now()
        => Assert.Equal(1, WorldCatchup.PropOwner(current: null, cached: 1, newcomer: 3, present: Here));

    [Fact]
    public void An_owner_who_has_left_is_passed_over()
        => Assert.Equal(1, WorldCatchup.PropOwner(current: 9, cached: 1, newcomer: 3, present: Here));

    [Fact]
    public void The_newcomer_is_not_named_because_their_small_id_was_used_before()
    {
        // Small ids are reused, so an old record can carry the newcomer's own id.
        Assert.Equal(1, WorldCatchup.PropOwner(current: 3, cached: 3, newcomer: 3, present: Here,
            anyoneElse: 1));
    }

    [Fact]
    public void Somebody_else_stands_in_when_both_owners_have_gone()
        => Assert.Equal(2, WorldCatchup.PropOwner(current: 9, cached: 8, newcomer: 3, present: Here,
            anyoneElse: 2));

    [Fact]
    public void The_newcomer_is_named_only_when_nobody_else_is_here()
        => Assert.Equal(3, WorldCatchup.PropOwner(current: null, cached: 8, newcomer: 3,
            present: id => id == 3, anyoneElse: null));

    [Fact]
    public void A_holstered_gun_nobody_holds_goes_back_on_the_hip()
        => Assert.True(WorldCatchup.ShouldReseat(Array.Empty<byte>()));

    [Fact]
    public void A_holstered_gun_in_somebodys_hand_is_not_put_back()
    {
        // The draw was missed, so the record is stale and the hip is empty.
        Assert.False(WorldCatchup.ShouldReseat(new byte[] { 3 }));
    }

    [Fact]
    public void A_request_to_the_owner_is_left_alone()
        => Assert.Null(WorldCatchup.DataRequestTarget(asked: 2, current: 2, requester: 3, present: Here));

    [Fact]
    public void A_request_to_an_owner_who_has_left_goes_to_the_owner_now()
        => Assert.Equal((byte?)2, WorldCatchup.DataRequestTarget(asked: 9, current: 2, requester: 3, present: Here));

    [Fact]
    public void A_request_to_an_old_owner_who_is_still_here_goes_to_the_new_one()
    {
        // It changed hands while the newcomer was loading.
        Assert.Equal((byte?)2, WorldCatchup.DataRequestTarget(asked: 1, current: 2, requester: 3, present: Here));
    }

    [Fact]
    public void A_request_to_player_zero_goes_to_the_real_owner()
        => Assert.Equal((byte?)2, WorldCatchup.DataRequestTarget(asked: 0, current: 2, requester: 3, present: Here));

    [Fact]
    public void A_request_with_no_target_goes_to_the_owner()
        => Assert.Equal((byte?)2, WorldCatchup.DataRequestTarget(asked: null, current: 2, requester: 3, present: Here));

    [Fact]
    public void An_owner_who_has_gone_is_not_redirected_to()
        => Assert.Null(WorldCatchup.DataRequestTarget(asked: 1, current: 9, requester: 3, present: Here));

    [Fact]
    public void An_entity_nobody_owns_leaves_the_request_alone()
        => Assert.Null(WorldCatchup.DataRequestTarget(asked: 1, current: null, requester: 3, present: Here));

    [Fact]
    public void A_player_is_never_sent_their_own_request()
        => Assert.Null(WorldCatchup.DataRequestTarget(asked: 1, current: 3, requester: 3, present: Here));
}
