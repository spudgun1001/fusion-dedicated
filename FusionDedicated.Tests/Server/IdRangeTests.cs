using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>The allocator gives up after one pass of the prop ids rather than looping for ever.</summary>
public class IdRangeTests
{
    private static EntityRegistry WithIdsTaken(IEnumerable<int> ids)
    {
        var registry = new EntityRegistry();

        foreach (int id in ids)
        {
            registry.Register((ushort)id, "Pack.Spawnable.Thing", 1, 0, 0, 0);
        }

        return registry;
    }

    private static IEnumerable<int> EveryPropId() => Enumerable.Range(256, 65280);

    [Fact]
    public void A_full_id_range_throws()
    {
        var registry = WithIdsTaken(EveryPropId());

        Assert.Throws<InvalidOperationException>(() => registry.AllocateId());
    }

    [Fact]
    public void The_top_id_is_handed_out_when_it_is_the_only_one_free()
    {
        var registry = WithIdsTaken(EveryPropId().Where(id => id != 65535));

        Assert.Equal((ushort)65535, registry.AllocateId());
    }

    [Fact]
    public void The_last_free_id_is_found_after_wrapping()
    {
        var registry = WithIdsTaken(EveryPropId().Where(id => id != 256));

        // The first call takes 256 without registering it, so the second has to go round the whole range to find it again.
        Assert.Equal((ushort)256, registry.AllocateId());
        Assert.Equal((ushort)256, registry.AllocateId());
    }
}
