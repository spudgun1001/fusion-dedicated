using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A cull flag is the owner's own word that it stopped simulating something. A new owner's
/// game reports its own, so the old one must not be kept and replayed under the new name.
/// </summary>
public class CullFlagOwnerTests
{
    private static EntityRegistry CulledCrateOwnedBy(byte owner)
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Pack.Spawnable.Crate", owner, 0, 0, 0);
        registry.SetCulledForOwner(300, true);
        return registry;
    }

    [Fact]
    public void A_new_owner_starts_with_the_flag_clear()
    {
        var registry = CulledCrateOwnedBy(1);

        registry.SetOwner(300, 2);

        Assert.False(registry.Get(300)!.CulledForOwner);
    }

    [Fact]
    public void The_same_owner_again_keeps_the_flag()
    {
        var registry = CulledCrateOwnedBy(1);

        registry.SetOwner(300, 1);

        Assert.True(registry.Get(300)!.CulledForOwner);
    }

    [Fact]
    public void Losing_the_owner_clears_the_flag()
    {
        var registry = CulledCrateOwnedBy(1);

        registry.SetOwner(300, null);

        Assert.False(registry.Get(300)!.CulledForOwner);
    }

    [Fact]
    public void An_heir_starts_with_the_flag_clear()
    {
        var registry = CulledCrateOwnedBy(1);

        registry.OrphanWith(1, _ => 2);

        Assert.False(registry.Get(300)!.CulledForOwner);
    }

    [Fact]
    public void No_heir_clears_the_flag_too()
    {
        var registry = CulledCrateOwnedBy(1);

        registry.OrphanWith(1, _ => null);

        Assert.False(registry.Get(300)!.CulledForOwner);
    }
}
