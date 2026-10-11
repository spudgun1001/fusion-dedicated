using FusionDedicated.Server;
using FusionDedicated.Tests.Web;
using FusionDedicated.Web;

namespace FusionDedicated.Tests.Server;

/// <summary>Barcodes on the never-cull list stay through the cull and eviction.</summary>
public class NeverCullTests
{
    private static readonly string[] Vehicles = { "vehicle" };

    private static EntityRegistry World(params (ushort Id, string Barcode, byte? Owner, bool Inherited)[] entities)
    {
        var registry = new EntityRegistry();

        foreach (var (id, barcode, owner, inherited) in entities)
        {
            var entity = registry.Register(id, barcode, 1, 0, 0, 0);
            registry.SetOwner(id, owner);
            entity.Inherited = inherited;
            entity.LastUpdate = DateTime.UtcNow.AddHours(-1);
        }

        return registry;
    }

    private static List<ushort> Cull(EntityRegistry registry, IReadOnlyCollection<string>? neverCull)
        => registry.CullStaleDetailed(
                TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(10),
                neverCull: neverCull)
            .Select(e => e.Id).ToList();

    [Fact]
    public void A_listed_orphan_stays_and_another_goes()
    {
        var registry = World((300, "Pack.Spawnable.Vehicle_Van", null, false), (301, "Pack.Spawnable.Chair", null, false));

        Assert.Equal(new ushort[] { 301 }, Cull(registry, Vehicles));
        Assert.NotNull(registry.Get(300));
    }

    [Fact]
    public void Inherited_and_idle_follow_the_same_rule()
    {
        var registry = World(
            (300, "Pack.Spawnable.VEHICLE.Car", 2, true),
            (301, "Pack.Spawnable.vehicle.Bike", 1, false),
            (302, "Pack.Spawnable.Crate", 2, true),
            (303, "Pack.Spawnable.Box", 1, false));

        Assert.Equal(new ushort[] { 302, 303 }, Cull(registry, Vehicles).Order());
    }

    [Fact]
    public void Without_the_list_everything_stale_goes()
    {
        var registry = World((300, "Pack.Spawnable.Vehicle_Van", null, false));

        Assert.Equal(new ushort[] { 300 }, Cull(registry, null));
    }

    [Fact]
    public void Eviction_never_takes_a_listed_entity_even_when_short()
    {
        var registry = World((300, "Pack.Spawnable.Vehicle_Van", null, false), (301, "Pack.Spawnable.Chair", null, false));

        Assert.Equal(new ushort[] { 301 }, registry.EvictOldest(5, neverCull: Vehicles));
        Assert.Empty(registry.EvictOldest(5, anyOwner: true, idleFor: TimeSpan.FromMinutes(2), neverCull: Vehicles));
        Assert.NotNull(registry.Get(300));
    }

    [Fact]
    public void The_config_defaults_to_empty()
        => Assert.Empty(new ServerConfig().NeverCullBarcodes);

    [Fact]
    public void The_panel_splits_lines_and_commas()
        => Assert.Equal(new[] { "Vehicle", "Car", "Van" }, Dashboard.SplitList("Vehicle\r\n Car, Van ,\n\n"));

    [Fact]
    public void The_panel_shows_and_saves_the_list()
    {
        Assert.Contains("neverCullBarcodes = _config.NeverCullBarcodes,", DashboardSource.Text());
        Assert.Contains("_config.NeverCullBarcodes = SplitList(", DashboardSource.Method("private void HandleSettings("));

        string page = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "FusionDedicated", "Web", "index.html"));

        Assert.Contains("$('fNeverCull').value = d.limits.neverCullBarcodes.join('\\n');", page);
        Assert.Contains("neverCullBarcodes: $('fNeverCull').value,", page);
    }

    [Fact]
    public void The_server_passes_the_list_to_the_cull_and_both_evictions()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "FusionDedicated", "Server", "FusionServer.cs"));

        Assert.Equal(3, source.Split("neverCull: Config.NeverCullBarcodes").Length - 1);
    }
}
