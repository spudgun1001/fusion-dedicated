using FusionDedicated.Server;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Server;

/// <summary>What a constraint's two ends hold, read from the create message.</summary>
public class ConstraintEndsTests
{
    private static ConstraintEnd Entity(ushort id) => new(ConstraintEndKind.Entity, id);

    [Fact]
    public void Two_entity_ends_are_read_with_their_ids()
    {
        var ends = ConstraintEnds.TryRead(ConstraintPayloads.Build(1, new EntityEnd(2, 5), new EntityEnd(2, 9)));

        Assert.NotNull(ends);
        Assert.Equal(Entity(2), ends.Value.First);
        Assert.Equal(Entity(2), ends.Value.Second);
    }

    [Fact]
    public void A_constrainer_id_is_stepped_over()
    {
        var ends = ConstraintEnds.TryRead(
            ConstraintPayloads.Build(1, new EntityEnd(300), new EntityEnd(301), constrainer: 777));

        Assert.NotNull(ends);
        Assert.Equal(Entity(300), ends.Value.First);
        Assert.Equal(Entity(301), ends.Value.Second);
    }

    [Fact]
    public void A_scene_end_is_read_and_the_end_after_it_is_found()
    {
        var ends = ConstraintEnds.TryRead(
            ConstraintPayloads.Build(1, new SceneEnd("/Level/Wall/Brick"), new EntityEnd(3)));

        Assert.NotNull(ends);
        Assert.Equal(ConstraintEndKind.Scene, ends.Value.First.Kind);
        Assert.Equal(Entity(3), ends.Value.Second);
    }

    [Fact]
    public void Null_ends_are_read()
    {
        var ends = ConstraintEnds.TryRead(ConstraintPayloads.Build(1, new NullEnd(), new NullEnd()));

        Assert.NotNull(ends);
        Assert.Equal(ConstraintEndKind.Null, ends.Value.First.Kind);
        Assert.Equal(ConstraintEndKind.Null, ends.Value.Second.Kind);
    }

    [Fact]
    public void An_empty_payload_does_not_read()
        => Assert.Null(ConstraintEnds.TryRead(Array.Empty<byte>()));

    [Fact]
    public void An_entity_end_cut_short_does_not_read()
        => Assert.Null(ConstraintEnds.TryRead(new byte[] { 1, 0, 0, 1, 0 }));

    [Fact]
    public void An_unknown_end_type_does_not_read()
        => Assert.Null(ConstraintEnds.TryRead(new byte[] { 1, 0, 0, 7, 0, 0, 0, 0 }));

    [Fact]
    public void A_scene_length_past_the_end_does_not_read()
        => Assert.Null(ConstraintEnds.TryRead(new byte[] { 1, 0, 0, 2, 0, 0, 0, 50, 65 }));

    [Fact]
    public void Player_and_prop_ends_are_told_apart()
    {
        Assert.False(Entity(0).IsPlayer);
        Assert.True(Entity(1).IsPlayer);
        Assert.True(Entity(255).IsPlayer);
        Assert.False(Entity(255).IsProp);
        Assert.True(Entity(256).IsProp);
        Assert.False(Entity(256).IsPlayer);
        Assert.False(new ConstraintEnd(ConstraintEndKind.Scene, 0).IsPlayer);
        Assert.False(new ConstraintEnd(ConstraintEndKind.Scene, 300).IsProp);
    }
}

/// <summary>A constraint kept for joiners, and when what it holds has gone.</summary>
public class StoredConstraintTests
{
    private const ulong Kanza = 76561198000000002;
    private const ulong Somebody = 76561198000000009;

    private static readonly (ConstraintEnd First, ConstraintEnd Second) HandToCrate =
        (new ConstraintEnd(ConstraintEndKind.Entity, 2), new ConstraintEnd(ConstraintEndKind.Entity, 300));

    [Fact]
    public void Player_ends_remember_who_held_the_small_id_and_prop_ends_their_id()
    {
        var stored = StoredConstraint.For(1, new byte[] { 9 }, 801, HandToCrate,
            smallId => smallId == 2 ? Kanza : null);

        Assert.Equal(Kanza, stored.Players[2]);
        Assert.Equal(new ushort[] { 300 }, stored.Props);
        Assert.True(stored.NamesPlayer(2));
        Assert.False(stored.NamesPlayer(1));
        Assert.True(stored.NamesProp(300));
        Assert.True(stored.Readable);
        Assert.Equal((ushort)801, stored.Partner);
    }

    [Fact]
    public void A_constraint_is_stale_when_its_small_id_changes_hands_or_its_prop_goes()
    {
        var stored = StoredConstraint.For(1, new byte[] { 9 }, 801, HandToCrate, _ => Kanza);

        Assert.False(stored.IsStale(_ => Kanza, _ => true));
        Assert.True(stored.IsStale(_ => Somebody, _ => true));
        Assert.True(stored.IsStale(_ => null, _ => true));
        Assert.True(stored.IsStale(_ => Kanza, _ => false));
    }

    [Fact]
    public void An_unreadable_constraint_names_nothing_and_is_never_stale()
    {
        var stored = StoredConstraint.For(1, new byte[] { 9 }, 801, null, _ => null);

        Assert.False(stored.Readable);
        Assert.Empty(stored.Players);
        Assert.Empty(stored.Props);
        Assert.False(stored.IsStale(_ => null, _ => false));
    }
}
