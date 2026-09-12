using FusionDedicated.Server.Props;

namespace FusionDedicated.Tests.Harness;

public class KeptPropTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-kept-props-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "persistent.json");

    public KeptPropTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Two_kept_props_stacked_on_top_of_each_other_both_register()
    {
        using var world = new World();
        var store = new PersistentPropStore(Path_);

        const string barcode = "DayTrip.Phone.Spawnable.Phone";

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 1f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 3f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        world.Server.Props = store;

        world.Join(1001, "Newcomer");

        var kept = world.Server.Entities.Entities
            .Count(e => e.Persistent && e.Barcode == barcode);

        Assert.Equal(2, kept);
    }

    [Fact]
    public void Two_kept_props_side_by_side_each_register_and_stay_two()
    {
        using var world = new World();
        var store = new PersistentPropStore(Path_);

        const string barcode = "DayTrip.Phone.Spawnable.Phone";

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 1f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1.2f,
            Y = 1f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        world.Server.Props = store;

        world.Join(1001, "First");

        Assert.Equal(2, world.Server.Entities.Entities.Count(e => e.Persistent && e.Barcode == barcode));

        world.Join(1002, "Second");

        Assert.Equal(2, world.Server.Entities.Entities.Count(e => e.Persistent && e.Barcode == barcode));
    }

    [Fact]
    public void A_kept_prop_that_has_settled_still_matches_its_own_record()
    {
        using var world = new World();
        var store = new PersistentPropStore(Path_);

        const string barcode = "DayTrip.Phone.Spawnable.Phone";

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 5f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        world.Server.Props = store;

        world.Join(1001, "First");

        var entity = world.Server.Entities.Entities.Single(e => e.Barcode == barcode);
        var owner = entity.OwnerSmallId ?? 0;

        // The way a pose moves it once it settles, falls, or is knocked, well past
        // the 0.5m tolerance.
        world.Server.Entities.NotePose(entity.Id, owner, entity.X, entity.Y - 2f, entity.Z);

        world.Join(1002, "Second");

        var kept = world.Server.Entities.Entities
            .Count(e => e.Persistent && e.Barcode == barcode);

        Assert.Equal(1, kept);
    }
}
