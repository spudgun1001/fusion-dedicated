using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>An owner's hand edit to a plugin's data file is loaded, not saved over.</summary>
public class PluginDataEditTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-dataedit-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = new();

    public PluginDataEditTests() => Directory.CreateDirectory(Path.Combine(_dir, "data"));

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

    /// <summary>Holds its value in a field and saves it on the way out, like most plugins.</summary>
    private sealed class Counter : IFusionPlugin
    {
        private PluginContext? _context;
        public int Value;
        public int Starts;

        public void Start(PluginContext context)
        {
            _context = context;
            Value = context.Store.Get<int>("n");
            Starts++;
        }

        public void Shutdown()
        {
            _context!.Store.Set("n", Value);
            _context.Store.Save();
        }
    }

    private string Data(string name) => Path.Combine(_dir, "data", name + ".json");

    private void Write(string name, string json, bool settled = true)
    {
        File.WriteAllText(Data(name), json);

        if (settled)
        {
            File.SetLastWriteTimeUtc(Data(name), DateTime.UtcNow.AddSeconds(-2));
        }
    }

    private PluginHost Host()
    {
        var health = new PluginHealth();
        void Log(string level, string message) => _log.Add($"{level} {message}");

        return new PluginHost(Path.Combine(_dir, "plugins"), Path.Combine(_dir, "data"),
            new PluginEvents(health, Log), health, new PluginPanel(health, Log),
            new PluginModules(health, Log), new NoActions(), () => Array.Empty<PluginPlayer>(), Log);
    }

    private PluginStore LoadedStore()
    {
        Write("s", """{ "n": 1 }""");
        var store = new PluginStore(Data("s"), (level, message) => _log.Add($"{level} {message}"));
        store.Load();
        return store;
    }

    [Fact]
    public void A_store_notices_its_file_was_edited()
    {
        var store = LoadedStore();

        Write("s", """{ "n": 22 }""");

        Assert.True(store.EditedOnDisk);
    }

    [Fact]
    public void Save_does_not_overwrite_an_edit()
    {
        var store = LoadedStore();
        Write("s", """{ "n": 22 }""");

        store.Set("n", 5);
        store.Save();
        store.Save();

        Assert.Contains("22", File.ReadAllText(Data("s")));
        Assert.Single(_log, l => l.StartsWith("WARN") && l.Contains("edited"));
    }

    [Fact]
    public void A_normal_save_is_not_an_edit()
    {
        var store = LoadedStore();

        store.Set("n", 5);
        store.Save();

        Assert.False(store.EditedOnDisk);
    }

    [Fact]
    public void Only_the_edited_plugin_is_reloaded_and_reads_the_edit()
    {
        Write("a", """{ "n": 1 }""");
        Write("b", """{ "n": 1 }""");
        var host = Host();
        var a = new Counter();
        var b = new Counter();
        host.LoadFromInstance("a", a);
        host.LoadFromInstance("b", b);

        Write("a", """{ "n": 22 }""");
        host.ReloadEdited();

        Assert.Equal(2, a.Starts);
        Assert.Equal(22, a.Value);
        Assert.Contains("22", File.ReadAllText(Data("a")));
        Assert.Equal(1, b.Starts);
        Assert.Equal(2, host.Loaded.Count);
        Assert.Contains(_log, l => l.StartsWith("INFO") && l.Contains("was edited on disk, so a was reloaded"));
    }

    [Fact]
    public void An_edit_still_being_written_waits_until_it_settles()
    {
        Write("a", """{ "n": 1 }""");
        var host = Host();
        var a = new Counter();
        host.LoadFromInstance("a", a);

        Write("a", """{ "n": 22 }""", settled: false);
        host.ReloadEdited();

        Assert.Equal(1, a.Starts);

        File.SetLastWriteTimeUtc(Data("a"), DateTime.UtcNow.AddSeconds(-2));
        host.ReloadEdited();

        Assert.Equal(2, a.Starts);
        Assert.Equal(22, a.Value);
    }

    [Fact]
    public void An_unreadable_edit_is_not_loaded_and_the_plugin_keeps_working()
    {
        Write("a", """{ "n": 7 }""");
        var host = Host();
        var a = new Counter();
        host.LoadFromInstance("a", a);

        Write("a", "{ broken");
        host.ReloadEdited();

        Assert.Equal(1, a.Starts);
        Assert.Equal(7, a.Value);
        Assert.Single(host.Loaded);
        Assert.Contains(_log, l => l.StartsWith("WARN") && l.Contains("could not be read"));
        Assert.False(host.Loaded[0].Store.EditedOnDisk);
    }
}
