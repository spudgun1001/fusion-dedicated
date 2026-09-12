using FusionDedicated.Server;
using FusionDedicated.Server.Props;

namespace FusionDedicated.Tests.Server;

public class PersistentPropTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-props-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "persistent.json");

    public PersistentPropTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static PersistentProp Bodymall(string level = "Museum", float x = 8.3f)
        => new()
        {
            Barcode = "DayTrip.PortableBodymall.Spawnable.Bodymall",
            Level = level,
            X = x,
            Y = 5.2f,
            Z = -14f,
            Rotation = "000FFF0102030A",
        };

    [Fact]
    public void A_prop_survives_being_written_and_read_back()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall());
        store.Save();

        var reopened = new PersistentPropStore(Path_);
        reopened.Load();

        var prop = reopened.All.Single();

        Assert.Equal("DayTrip.PortableBodymall.Spawnable.Bodymall", prop.Barcode);
        Assert.Equal(5.2f, prop.Y);
    }

    [Fact]
    public void The_rotation_comes_back_as_the_bytes_it_went_in_as()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall());
        store.Save();

        var reopened = new PersistentPropStore(Path_);
        reopened.Load();

        Assert.Equal(new byte[] { 0x00, 0x0F, 0xFF, 0x01, 0x02, 0x03, 0x0A },
            reopened.All.Single().RotationBytes());
    }

    [Fact]
    public void A_rotation_that_will_not_parse_reads_as_none_rather_than_throwing()
    {
        var prop = Bodymall();
        prop.Rotation = "not hex";

        Assert.Empty(prop.RotationBytes());
    }

    [Fact]
    public void Only_the_props_for_this_level_come_back()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall("Museum"));
        store.Add(Bodymall("GunRange"));

        Assert.Single(store.For("Museum"));
        Assert.Equal("Museum", store.For("Museum").Single().Level);
    }

    [Fact]
    public void A_level_with_nothing_placed_on_it_gets_nothing()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall("Museum"));

        Assert.Empty(store.For("Tuscany"));
    }

    [Fact]
    public void A_prop_can_be_removed_by_where_it_is()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall());

        Assert.True(store.Remove(
            "DayTrip.PortableBodymall.Spawnable.Bodymall", "Museum", 8.3f, 5.2f, -14f));

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Removing_matches_a_prop_that_settled_a_little_after_being_dropped()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall());

        Assert.True(store.Remove(
            "DayTrip.PortableBodymall.Spawnable.Bodymall", "Museum", 8.4f, 5.1f, -14.1f));
    }

    [Fact]
    public void Removing_leaves_an_identical_prop_somewhere_else_alone()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall(x: 8.3f));
        store.Add(Bodymall(x: 40f));

        store.Remove("DayTrip.PortableBodymall.Spawnable.Bodymall", "Museum", 8.3f, 5.2f, -14f);

        Assert.Equal(40f, store.All.Single().X);
    }

    [Fact]
    public void Removing_takes_the_closest_of_two_props_side_by_side()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall(x: 8.3f));
        store.Add(Bodymall(x: 8.5f));

        Assert.True(store.Remove("DayTrip.PortableBodymall.Spawnable.Bodymall", "Museum", 8.5f, 5.2f, -14f));

        Assert.Equal(8.3f, store.All.Single().X);
    }

    [Fact]
    public void A_file_that_will_not_parse_keeps_what_is_already_loaded()
    {
        var store = new PersistentPropStore(Path_);
        store.Add(Bodymall());
        store.Save();

        File.WriteAllText(Path_, "{ not json");
        store.Load();

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void A_persistent_entity_is_not_removable()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Test.Prop", 1, 1, 2, 3);
        registry.Get(300)!.Persistent = true;

        Assert.False(registry.Get(300)!.Removable);
    }

    [Fact]
    public void Clear_all_leaves_a_persistent_prop_standing()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Test.Prop", 1, 1, 2, 3);
        registry.Register(301, "Test.Other", 1, 1, 2, 3);
        registry.Get(300)!.Persistent = true;

        var removed = registry.Clear();

        Assert.Equal(new ushort[] { 301 }, removed);
        Assert.NotNull(registry.Get(300));
    }

    [Fact]
    public void No_cull_takes_a_persistent_prop_however_long_it_sits()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Test.Prop", 1, 1, 2, 3);

        var prop = registry.Get(300)!;
        prop.Persistent = true;
        prop.LastUpdate = DateTime.UtcNow.AddDays(-1);

        registry.SetOwner(300, null);
        prop.LastUpdate = DateTime.UtcNow.AddDays(-1);

        Assert.Empty(registry.CullStale(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

        Assert.Empty(registry.CullOrphans(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Eviction_at_the_cap_leaves_a_persistent_prop_standing()
    {
        var registry = new EntityRegistry();
        registry.Register(300, "Test.Prop", 1, 1, 2, 3);
        registry.Get(300)!.Persistent = true;
        registry.SetOwner(300, null);

        Assert.Empty(registry.EvictOldest(10));
    }

    [Fact]
    public void A_level_change_takes_persistent_props_with_it()
    {
        // The old world is gone whatever it was made of. The props go back on the
        // level they belong to, from the file, once somebody arrives there.
        var registry = new EntityRegistry();
        registry.Register(300, "Test.Prop", 1, 1, 2, 3);
        registry.Register(301, "Test.Other", 1, 1, 2, 3);
        registry.Get(300)!.Persistent = true;

        Assert.Equal(2, registry.Forget().Count);
        Assert.Null(registry.Get(300));
    }

    [Fact]
    public void An_ordinary_orphan_is_still_culled()
    {
        var registry = new EntityRegistry();
        registry.Register(301, "Test.Other", 1, 1, 2, 3);
        registry.SetOwner(301, null);
        registry.Get(301)!.LastUpdate = DateTime.UtcNow.AddDays(-1);

        Assert.Equal(new ushort[] { 301 }, registry.CullOrphans(TimeSpan.FromSeconds(1)));
    }
}
