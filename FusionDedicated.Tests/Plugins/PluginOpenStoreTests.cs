using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>A second store beside a plugin's main one, for data too big or too busy to rewrite on every save.</summary>
public class PluginOpenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-openstore-" + Guid.NewGuid().ToString("N"));

    public PluginOpenStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class NoActions : IPluginActions
    {
        public void Kick(ulong platformId, string reason) { }
        public void Ban(ulong platformId, string reason) { }
        public void SetRank(ulong platformId, PermissionLevel level) { }
        public void Despawn(ushort entityId) { }
        public void SendModule(ulong platformId, long tag, byte[] payload) { }
        public void BroadcastModule(long tag, byte[] payload) { }
    }

    private PluginContext Context()
        => new("labrp",
            new PluginEvents(new PluginHealth(), (_, _) => { }),
            new PluginStore(Path.Combine(_dir, "labrp.json")),
            new PluginPanel(new PluginHealth(), (_, _) => { }),
            new PluginModules(new PluginHealth(), (_, _) => { }),
            new NoActions(),
            () => Array.Empty<PluginPlayer>(),
            (_, _) => { });

    [Fact]
    public void An_opened_store_is_written_beside_the_main_one()
    {
        var store = Context().OpenStore("audit");
        store.Set("count", 3);
        store.Save();

        Assert.True(File.Exists(Path.Combine(_dir, "labrp.audit.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "labrp.json")));
    }

    [Fact]
    public void An_opened_store_loads_what_is_already_there()
    {
        var earlier = new PluginStore(Path.Combine(_dir, "labrp.audit.json"));
        earlier.Set("count", 7);
        earlier.Save();

        Assert.Equal(7, Context().OpenStore("audit").Get<int>("count"));
    }

    [Fact]
    public void Asking_twice_for_the_same_part_gives_the_same_store()
    {
        var context = Context();

        Assert.Same(context.OpenStore("audit"), context.OpenStore("audit"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("audit.json")]
    [InlineData("abcdefghijabcdefghijabcdefghijabc")]
    public void A_part_that_is_not_a_plain_name_is_refused(string part)
        => Assert.Throws<ArgumentException>(() => Context().OpenStore(part));

    [Fact]
    public void Saving_opened_stores_writes_each_of_them()
    {
        var context = Context();
        context.OpenStore("audit").Set("count", 1);

        context.SaveOpenedStores();

        Assert.True(File.Exists(Path.Combine(_dir, "labrp.audit.json")));
    }

    private sealed class AuditPlugin : IFusionPlugin
    {
        public void Start(PluginContext context) => context.OpenStore("audit").Set("count", 5);

        public void Shutdown()
        {
        }
    }

    [Fact]
    public void Unloading_a_plugin_saves_the_stores_it_opened()
    {
        var health = new PluginHealth();
        var host = new PluginHost(Path.Combine(_dir, "plugins"),
            new PluginEvents(health, (_, _) => { }), health,
            new PluginPanel(health, (_, _) => { }),
            new PluginModules(health, (_, _) => { }),
            new NoActions(), () => Array.Empty<PluginPlayer>(), (_, _) => { });

        Assert.True(host.LoadFromInstance("labrp", new AuditPlugin()));
        Assert.True(host.Unload("labrp"));

        var saved = new PluginStore(Path.Combine(_dir, "plugin-data", "labrp.audit.json"));
        saved.Load();

        Assert.Equal(5, saved.Get<int>("count"));
    }
}
