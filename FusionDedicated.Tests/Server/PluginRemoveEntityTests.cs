using FusionDedicated.Server;
using FusionDedicated.Server.Props;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A plugin removing a prop. A kept one has to stop being kept as well, the way the
/// panel's Remove already does, or it is put straight back after the next restart.
/// </summary>
public class PluginRemoveEntityTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-remove-" + Guid.NewGuid().ToString("N"));

    public PluginRemoveEntityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private FusionServer Server(out PersistentPropStore store)
    {
        store = new PersistentPropStore(Path.Combine(_dir, "persistent.json"));

        return new FusionServer(new ServerConfig { LevelBarcode = "Museum" }) { Props = store };
    }

    [Fact]
    public void A_kept_prop_a_plugin_removes_is_not_put_back()
    {
        var server = Server(out var store);
        server.Entities.Register(300, "spudgun1001.Payphone.Spawnable.PayphoneWall", 1, 1f, 2f, 3f);
        server.KeepProp(300, "");

        Assert.True(server.RemoveEntity(300));

        Assert.Empty(store.For("Museum"));
        Assert.Null(server.Entities.Get(300));
    }

    [Fact]
    public void An_ordinary_prop_is_simply_removed()
    {
        var server = Server(out _);
        server.Entities.Register(300, "Pack.Spawnable.Thing", 1, 0f, 0f, 0f);

        Assert.True(server.RemoveEntity(300));

        Assert.Null(server.Entities.Get(300));
    }

    [Fact]
    public void A_prop_that_is_not_there_is_not_removed()
        => Assert.False(Server(out _).RemoveEntity(9000));
}
