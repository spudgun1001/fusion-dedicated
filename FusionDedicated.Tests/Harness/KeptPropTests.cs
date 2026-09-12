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
}
