using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The registry saying which props left, however they left. The server frees each
/// one's saved RPC variables when it hears.
/// </summary>
public class EntityRemovalNoticeTests
{
    private static (EntityRegistry Registry, List<ushort> Heard) Abandoned(int count)
    {
        var registry = new EntityRegistry();
        var heard = new List<ushort>();
        registry.Removed += id => heard.Add(id);

        for (int i = 0; i < count; i++)
        {
            ushort id = (ushort)(EntityRegistry.FirstEntityId + i);
            var entity = registry.Register(id, "Pack.Spawnable.Thing", 1, 0, 0, 0);

            registry.SetOwner(id, null);
            entity.LastUpdate = DateTime.UtcNow.AddHours(-1);
        }

        return (registry, heard);
    }

    [Fact]
    public void Removing_a_prop_says_so()
    {
        var (registry, heard) = Abandoned(2);

        registry.Remove(EntityRegistry.FirstEntityId);

        Assert.Equal(new[] { EntityRegistry.FirstEntityId }, heard);
    }

    [Fact]
    public void Removing_a_prop_that_is_not_there_says_nothing()
    {
        var (registry, heard) = Abandoned(1);

        registry.Remove(9000);

        Assert.Empty(heard);
    }

    [Fact]
    public void Evicting_says_which_went()
    {
        var (registry, heard) = Abandoned(3);

        var removed = registry.EvictOldest(2);

        Assert.Equal(removed, heard);
    }

    [Fact]
    public void Culling_orphans_says_which_went()
    {
        var (registry, heard) = Abandoned(2);

        var removed = registry.CullOrphans(TimeSpan.FromMinutes(1));

        Assert.Equal(2, removed.Count);
        Assert.Equal(removed, heard);
    }

    [Fact]
    public void Culling_stale_props_says_which_went()
    {
        var (registry, heard) = Abandoned(2);

        var removed = registry.CullStale(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        Assert.Equal(2, removed.Count);
        Assert.Equal(removed, heard);
    }

    [Fact]
    public void Clearing_says_which_went()
    {
        var (registry, heard) = Abandoned(3);

        var removed = registry.Clear();

        Assert.Equal(removed, heard);
    }

    [Fact]
    public void Forgetting_the_level_says_which_went()
    {
        var (registry, heard) = Abandoned(3);

        var removed = registry.Forget();

        Assert.Equal(removed, heard);
    }
}
