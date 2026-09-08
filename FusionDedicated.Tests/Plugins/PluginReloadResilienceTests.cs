using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// A reload has to finish even when a plugin misbehaves on the way out.
/// </summary>
public class PluginReloadResilienceTests
{
    private sealed class Rude : IFusionPlugin
    {
        public bool Started;

        public void Start(PluginContext context) => Started = true;

        public void Shutdown() => throw new UnauthorizedAccessException(
            "Access to the path '/home/container/plugins/labrp/data.json' is denied.");
    }

    private sealed class Polite : IFusionPlugin
    {
        public bool Stopped;

        public void Start(PluginContext context) { }

        public void Shutdown() => Stopped = true;
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

    private static (PluginHost Host, List<string> Log, string Dir) Build()
    {
        var log = new List<string>();
        void Write(string level, string message) => log.Add($"{level} {message}");

        var health = new PluginHealth();
        // A root of its own, because saved data now sits beside the plugins folder
        // rather than inside it, and two tests sharing that would leak into each
        // other.
        string dir = Path.Combine(
            Path.GetTempPath(), "fusion-plugins-" + Guid.NewGuid().ToString("N"), "plugins");

        var host = new PluginHost(
            dir,
            new PluginEvents(health, Write), health,
            new PluginPanel(health, Write), new PluginModules(health, Write),
            new NoActions(), () => Array.Empty<PluginPlayer>(), Write);

        return (host, log, dir);
    }

    /// <summary>
    /// Makes a plugin's saved data unwritable, the way the live one was. A
    /// directory where the file should be is refused the same way a permission
    /// is, and needs no privileges to set up.
    /// </summary>
    private static void BlockTheStore(string dir, string plugin)
        => Directory.CreateDirectory(
            Path.Combine(Path.GetFullPath(Path.Combine(dir, "..", "plugin-data")), plugin + ".json"));

    [Fact]
    public void A_plugin_that_throws_on_the_way_out_is_still_unloaded()
    {
        var (host, log, _) = Build();
        host.LoadFromInstance("labrp", new Rude());

        Assert.True(host.Unload("labrp"));
        Assert.Empty(host.Loaded);
        Assert.Contains(log, l => l.Contains("threw while shutting down"));
    }

    [Fact]
    public void One_bad_plugin_does_not_stop_the_others_unloading()
    {
        // The live failure: labrp threw, and police and avatars were never
        // reached because the exception left ReloadAll entirely.
        var (host, _, dir) = Build();
        var police = new Polite();

        host.LoadFromInstance("labrp", new Rude());
        host.LoadFromInstance("police", police);

        host.ReloadAll();

        Assert.True(police.Stopped);
        Assert.Empty(host.Loaded);
    }

    [Fact]
    public void Reloading_says_what_it_ended_up_with_rather_than_throwing()
    {
        var (host, _, dir) = Build();
        host.LoadFromInstance("labrp", new Rude());

        // The directory does not exist, so nothing loads back. The point is that
        // it returns a count instead of taking the command down with it.
        Assert.Equal(0, host.ReloadAll());
    }

    [Fact]
    public void The_store_of_a_plugin_that_threw_is_still_let_go()
    {
        var (host, log, _) = Build();
        host.LoadFromInstance("labrp", new Rude());

        host.Unload("labrp");

        Assert.Contains(log, l => l.Contains("unloaded"));
    }

    [Fact]
    public void An_unwritable_data_json_does_not_take_the_reload_down()
    {
        // Exactly what happened live: Shutdown threw and was caught, then
        // Store.Save threw from outside the catch, left Unload, left ReloadAll,
        // and surfaced as "Command failed" with the old assembly still loaded.
        var (host, log, dir) = Build();

        BlockTheStore(dir, "labrp");
        host.LoadFromInstance("labrp", new Rude());

        var thrown = Record.Exception(() => host.ReloadAll());

        Assert.Null(thrown);
        Assert.Empty(host.Loaded);
        Assert.Contains(log, l => l.Contains("Nothing can be saved"));
    }

    [Fact]
    public void The_plugins_beside_it_are_reloaded_anyway()
    {
        var (host, _, dir) = Build();
        var police = new Polite();

        BlockTheStore(dir, "labrp");
        host.LoadFromInstance("labrp", new Rude());
        host.LoadFromInstance("police", police);

        host.ReloadAll();

        Assert.True(police.Stopped);
    }
}
