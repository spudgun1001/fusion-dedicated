using FusionDedicated.Server;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class DespawnEndpointTests
{
    [Fact]
    public void Removing_one_entity_needs_a_moderator()
    {
        Assert.False(PanelPermissions.Allows(PanelRole.Viewer, "/api/despawn"));
        Assert.True(PanelPermissions.Allows(PanelRole.Moderator, "/api/despawn"));
        Assert.True(PanelPermissions.Allows(PanelRole.Owner, "/api/despawn"));
    }

    [Fact]
    public void One_entity_can_be_taken_out_without_touching_the_rest()
    {
        var registry = new EntityRegistry();
        registry.Register(10, "Pack.Spawnable.Crate", 1, 0, 0, 0);
        registry.Register(11, "Pack.Spawnable.Gun", 1, 0, 0, 0);

        Assert.True(registry.Remove(10));

        Assert.Null(registry.Get(10));
        Assert.NotNull(registry.Get(11));
    }

    [Fact]
    public void Removing_something_that_has_already_gone_says_so()
        => Assert.False(new EntityRegistry().Remove(99));

    [Fact]
    public void A_persistent_prop_can_be_removed_by_asking_for_it()
    {
        // The culls leave it alone, but a deliberate press is not a cull.
        var registry = new EntityRegistry();
        registry.Register(10, "DayTrip.PortableBodymall.Spawnable.Bodymall", 1, 0, 0, 0);
        registry.Get(10)!.Persistent = true;

        Assert.True(registry.Remove(10));
        Assert.Null(registry.Get(10));
    }
}
