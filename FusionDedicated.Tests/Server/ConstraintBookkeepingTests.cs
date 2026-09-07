using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A constraint is two entities on the server and one id on the wire. The delete
/// names one of them, and clients drop both, so the server has to as well or the
/// other end sits on the books for the rest of the session counting against the
/// cap with nothing able to remove it.
/// </summary>
public class ConstraintBookkeepingTests
{
    private static (EntityRegistry Registry, ushort First, ushort Second) Constrained()
    {
        var registry = new EntityRegistry();

        var first = registry.Register(300, "fusion.constraint", 1, 0, 0, 0);
        var second = registry.Register(301, "fusion.constraint", 1, 0, 0, 0);

        first.Synthetic = true;
        second.Synthetic = true;
        first.Partner = 301;
        second.Partner = 300;

        return (registry, 300, 301);
    }

    [Fact]
    public void Each_end_knows_the_other()
    {
        var (registry, first, second) = Constrained();

        Assert.Equal(second, registry.Get(first)!.Partner);
        Assert.Equal(first, registry.Get(second)!.Partner);
    }

    [Fact]
    public void Removing_the_named_end_leaves_the_partner_findable()
    {
        // What the delete handler relies on: read the partner before removing.
        var (registry, first, second) = Constrained();

        ushort? partner = registry.Get(first)!.Partner;
        registry.Remove(first);

        Assert.Equal(second, partner);
        Assert.NotNull(registry.Get(second));
    }

    [Fact]
    public void Both_ends_gone_leaves_nothing_behind()
    {
        var (registry, first, second) = Constrained();

        registry.Remove(first);
        registry.Remove(second);

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void An_ordinary_prop_has_no_partner()
        => Assert.Null(new EntityRegistry()
            .Register(300, "Pack.Spawnable.Crate", 1, 0, 0, 0).Partner);
}

/// <summary>
/// Persistent props are deliberately left ownerless, and a client registers what
/// it is told it owns with its own update loop. Naming the arriving player meant
/// every joiner took ownership of every ownerless prop and they all simulated the
/// same object against each other.
/// </summary>
public class CatchupOwnershipTests
{
    [Fact]
    public void An_ownerless_prop_is_adopted_once_and_keeps_that_owner()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "DayTrip.PortableBodymall.Spawnable.Bodymall", 1, 0, 0, 0);
        registry.SetOwner(300, null);

        Assert.Null(registry.Get(300)!.OwnerSmallId);

        registry.SetOwner(300, 3);

        Assert.Equal((byte?)3, registry.Get(300)!.OwnerSmallId);
    }

    [Fact]
    public void An_owned_prop_keeps_the_owner_it_had()
    {
        // Only ownerless ones are adopted; everything else is replayed as it is,
        // so the newcomer agrees with everybody else about who simulates it.
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Gun", 5, 0, 0, 0);

        Assert.Equal((byte?)5, registry.Get(300)!.OwnerSmallId);
    }
}
