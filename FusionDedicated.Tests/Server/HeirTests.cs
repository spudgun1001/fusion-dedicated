using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// When somebody leaves, everything they owned went to the first player on the
/// list. A car went to a bystander instead of the passenger sitting in it, whose
/// own game then rejected the new owner's poses.
/// </summary>
public class HeirTests
{
    private const byte Leaver = 1;

    private static readonly byte[] Nobody = Array.Empty<byte>();

    [Fact]
    public void A_vehicle_goes_to_somebody_still_sitting_in_it()
    {
        Assert.Equal((byte?)4, WorldCatchup.HeirFor(new byte[] { Leaver, 4 }, Nobody, 2, Leaver));
    }

    [Fact]
    public void A_rider_comes_before_a_holder()
    {
        Assert.Equal((byte?)5, WorldCatchup.HeirFor(new byte[] { 5 }, new byte[] { 6 }, 2, Leaver));
    }

    [Fact]
    public void A_held_thing_goes_to_somebody_else_holding_it()
    {
        Assert.Equal((byte?)6, WorldCatchup.HeirFor(Nobody, new byte[] { Leaver, 6 }, 2, Leaver));
    }

    [Fact]
    public void Otherwise_it_goes_to_the_first_player_left()
    {
        Assert.Equal((byte?)2, WorldCatchup.HeirFor(Nobody, Nobody, 2, Leaver));
    }

    [Fact]
    public void The_leaver_is_never_their_own_heir()
    {
        Assert.Null(WorldCatchup.HeirFor(new byte[] { Leaver }, new byte[] { Leaver }, Leaver, Leaver));
    }

    [Fact]
    public void With_nobody_left_there_is_no_heir()
    {
        Assert.Null(WorldCatchup.HeirFor(Nobody, Nobody, null, Leaver));
    }

    [Fact]
    public void Each_entity_can_go_to_its_own_heir()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Atv", Leaver, 0, 0, 0);
        registry.Register(301, "Pack.Spawnable.Crate", Leaver, 0, 0, 0);

        var affected = registry.OrphanWith(Leaver, entity => entity.Id == 300 ? (byte)4 : (byte)2);

        Assert.Equal(2, affected.Count);
        Assert.Equal((byte?)4, registry.Get(300)!.OwnerSmallId);
        Assert.Equal((byte?)2, registry.Get(301)!.OwnerSmallId);
        Assert.True(registry.Get(300)!.Inherited);
    }

    [Fact]
    public void No_heir_leaves_it_orphaned_rather_than_inherited()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Crate", Leaver, 0, 0, 0);

        registry.OrphanWith(Leaver, _ => null);

        Assert.True(registry.Get(300)!.IsOrphaned);
        Assert.False(registry.Get(300)!.Inherited);
    }

    [Fact]
    public void Only_the_leavers_entities_are_handed_on()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Crate", 2, 0, 0, 0);

        var affected = registry.OrphanWith(Leaver, _ => 5);

        Assert.Empty(affected);
        Assert.Equal((byte?)2, registry.Get(300)!.OwnerSmallId);
    }

    [Fact]
    public void The_single_heir_form_still_hands_everything_to_one_player()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Crate", Leaver, 0, 0, 0);
        registry.Register(301, "Pack.Spawnable.Crate", Leaver, 0, 0, 0);

        registry.Orphan(Leaver, 3);

        Assert.Equal((byte?)3, registry.Get(300)!.OwnerSmallId);
        Assert.Equal((byte?)3, registry.Get(301)!.OwnerSmallId);
    }
}
