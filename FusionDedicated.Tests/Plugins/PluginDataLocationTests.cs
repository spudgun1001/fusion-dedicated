using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// Where a plugin's saved data lives.
///
/// It used to be inside the plugin's own folder, which meant two things. Updating
/// a plugin is replacing that folder, so an update took everybody's balances with
/// it. And a folder uploaded through a panel is often owned by somebody other than
/// the account the server runs as, so nothing could be saved at all: the live
/// server ran for a day warning that every change would be lost at shutdown.
/// </summary>
public sealed class PluginDataLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fusion-plugindata-" + Guid.NewGuid().ToString("N"));

    private string Plugins => Path.Combine(_root, "plugins");

    private string Data => Path.Combine(_root, "plugin-data");

    private readonly List<string> _log = new();

    private readonly PluginHealth _health = new();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* a test temp dir */ }
    }

    private PluginHost Host()
    {
        void Write(string level, string message) => _log.Add($"{level} {message}");

        return new PluginHost(Plugins, Data,
            new PluginEvents(_health, Write), _health,
            new PluginPanel(_health, Write), new PluginModules(_health, Write),
            new NoActions(), () => Array.Empty<PluginPlayer>(), Write);
    }

    private sealed class Saver : IFusionPlugin
    {
        public PluginContext? Context;

        public void Start(PluginContext context)
        {
            Context = context;
            Held = context.Store.Get<long>("balance");
        }

        public long Held { get; private set; }

        public void Shutdown() { }
    }

    private sealed class NoActions : IPluginActions
    {
        public void Kick(ulong platformId, string reason) { }
        public void Ban(ulong platformId, string reason) { }
        public void SetRank(ulong platformId, PermissionLevel level) { }
        public void Despawn(ushort entityId) { }
        public void SendModule(ulong platformId, long handlerTag, byte[] payload) { }
        public void BroadcastModule(long handlerTag, byte[] payload) { }
    }

    private void WriteOldData(string plugin, long balance)
    {
        string folder = Path.Combine(Plugins, plugin);

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "data.json"), $"{{\"balance\": {balance}}}");
    }

    [Fact]
    public void New_data_is_written_outside_the_plugin_folder()
    {
        var plugin = new Saver();
        Host().LoadFromInstance("labrp", plugin);

        plugin.Context!.Store.Set("balance", 500L);
        plugin.Context.Store.Save();

        Assert.True(File.Exists(Path.Combine(Data, "labrp.json")));
        Assert.False(File.Exists(Path.Combine(Plugins, "labrp", "data.json")));
    }

    [Fact]
    public void What_was_already_saved_inside_the_plugin_is_brought_across()
    {
        WriteOldData("labrp", 1234);

        var plugin = new Saver();
        Host().LoadFromInstance("labrp", plugin);

        Assert.Equal(1234, plugin.Held);
        Assert.True(File.Exists(Path.Combine(Data, "labrp.json")));
        Assert.Contains(_log, l => l.Contains("Moved labrp's saved data"));
    }

    [Fact]
    public void The_old_file_is_copied_rather_than_moved()
    {
        // The folder it sits in is often the very thing that could not be written
        // to, so a move would fail and leave the data stranded.
        WriteOldData("labrp", 1234);

        Host().LoadFromInstance("labrp", new Saver());

        Assert.True(File.Exists(Path.Combine(Plugins, "labrp", "data.json")));
    }

    [Fact]
    public void It_is_brought_across_once_and_never_again()
    {
        // Otherwise the stale copy left in the plugin folder would overwrite
        // everything that happened since, on every restart.
        WriteOldData("labrp", 1234);

        var first = new Saver();
        var host = Host();
        host.LoadFromInstance("labrp", first);

        first.Context!.Store.Set("balance", 9999L);
        first.Context.Store.Save();
        host.Unload("labrp");

        var second = new Saver();
        Host().LoadFromInstance("labrp", second);

        Assert.Equal(9999, second.Held);
    }

    [Fact]
    public void A_plugin_with_nothing_saved_yet_starts_clean()
    {
        var plugin = new Saver();
        Host().LoadFromInstance("labrp", plugin);

        Assert.Equal(0, plugin.Held);
        Assert.DoesNotContain(_log, l => l.Contains("Moved"));
    }

    [Fact]
    public void Two_plugins_keep_their_own_data()
    {
        var host = Host();
        var one = new Saver();
        var two = new Saver();

        host.LoadFromInstance("labrp", one);
        host.LoadFromInstance("police", two);

        one.Context!.Store.Set("balance", 111L);
        one.Context.Store.Save();
        two.Context!.Store.Set("balance", 222L);
        two.Context.Store.Save();

        Assert.True(File.Exists(Path.Combine(Data, "labrp.json")));
        Assert.True(File.Exists(Path.Combine(Data, "police.json")));
    }

    [Fact]
    public void An_unwritable_new_location_says_so_rather_than_taking_the_server_down()
    {
        // A directory where the file should be is refused the same way a
        // permission is, and needs no privileges to set up.
        Directory.CreateDirectory(Path.Combine(Data, "labrp.json"));

        var plugin = new Saver();
        Host().LoadFromInstance("labrp", plugin);

        plugin.Context!.Store.Set("balance", 500L);
        plugin.Context.Store.Save();

        Assert.Contains(_log, l => l.Contains("Nothing can be saved"));
    }
}
