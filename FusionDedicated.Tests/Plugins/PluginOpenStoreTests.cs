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

    private sealed class ManifestReaderPlugin : IFusionPlugin
    {
        public void Start(PluginContext context) => context.OpenStore("audit");

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

    [Fact]
    public void Unloading_does_not_overwrite_an_opened_store_that_changed_on_disk_after_load()
    {
        var health = new PluginHealth();
        var host = new PluginHost(Path.Combine(_dir, "plugins"),
            new PluginEvents(health, (_, _) => { }), health,
            new PluginPanel(health, (_, _) => { }),
            new PluginModules(health, (_, _) => { }),
            new NoActions(), () => Array.Empty<PluginPlayer>(), (_, _) => { });

        string auditPath = Path.Combine(_dir, "plugin-data", "labrp.audit.json");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        File.WriteAllText(auditPath, "{\"count\":1}");

        // Reads the manifest through OpenStore but never writes to it, the
        // same as doors and southside reading their level manifests.
        Assert.True(host.LoadFromInstance("labrp", new ManifestReaderPlugin()));

        // The owner rebuilds the level and copies in a new manifest while the
        // plugin is loaded and holding the old one in memory.
        File.WriteAllText(auditPath, "{\"count\":99}");

        Assert.True(host.Unload("labrp"));

        var saved = new PluginStore(auditPath);
        saved.Load();

        Assert.Equal(99, saved.Get<int>("count"));
    }

    [Fact]
    public void Unloading_does_not_write_an_opened_store_whose_file_did_not_exist_at_load()
    {
        var health = new PluginHealth();
        var host = new PluginHost(Path.Combine(_dir, "plugins"),
            new PluginEvents(health, (_, _) => { }), health,
            new PluginPanel(health, (_, _) => { }),
            new PluginModules(health, (_, _) => { }),
            new NoActions(), () => Array.Empty<PluginPlayer>(), (_, _) => { });

        // The first-deploy order: the plugin starts before the manifest exists.
        Assert.True(host.LoadFromInstance("labrp", new ManifestReaderPlugin()));

        // The owner copies the manifest in while the plugin is already running.
        string auditPath = Path.Combine(_dir, "plugin-data", "labrp.audit.json");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        File.WriteAllText(auditPath, "{\"count\":42}");

        Assert.True(host.Unload("labrp"));

        var saved = new PluginStore(auditPath);
        saved.Load();

        Assert.Equal(42, saved.Get<int>("count"));
    }
}
