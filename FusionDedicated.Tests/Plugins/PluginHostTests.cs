using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-pluginhost-" + Guid.NewGuid().ToString("N"), "plugins");

    private readonly List<string> _log = new();
    private readonly PluginHealth _health = new();
    private readonly PluginPanel _panel;
    private readonly PluginModules _modules;

    public PluginHostTests()
    {
        Directory.CreateDirectory(_dir);
        _panel = new PluginPanel(_health, (_, _) => { });
        _modules = new PluginModules(_health, (_, _) => { });
    }

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

    private sealed class Spy : IFusionPlugin
    {
        public bool Started;
        public bool Stopped;
        public bool ThrowOnStart;

        public void Start(PluginContext context)
        {
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("boom");
            }

            Started = true;
            context.Events.Spawn.Subscribe(context.Name, _ => PluginVerdict.Refuse("no spawning"));
        }

        public void Shutdown() => Stopped = true;
    }

    private sealed class RudeShutdown : IFusionPlugin
    {
        public void Start(PluginContext context) { }
        public void Shutdown() => throw new InvalidOperationException("boom");
    }

    private PluginHost Host(PluginEvents events)
        => new(_dir, events, _health, _panel, _modules, new NoActions(),
            () => Array.Empty<PluginPlayer>(),
            (level, message) => _log.Add(level + " " + message));

    private PluginEvents Events() => new(_health, (_, _) => { });

    private sealed class PagePlugin : IFusionPlugin
    {
        public void Start(PluginContext context)
        {
            context.Panel.Register(context.Name, () => new PluginPage("Police"));
            context.Panel.OnAction(context.Name, "add", _ => { });
        }

        public void Shutdown() { }
    }

    private sealed class BridgePlugin : IFusionPlugin
    {
        public void Start(PluginContext context)
            => context.Modules.Handle(context.Name, 4242, _ => ModuleAction.Drop);

        public void Shutdown() { }
    }

    [Fact]
    public void A_plugins_module_claim_goes_when_it_is_unloaded()
    {
        var host = Host(Events());

        host.LoadFromInstance("labrp", new BridgePlugin());

        Assert.True(_modules.Claims(4242));

        host.Unload("labrp");

        Assert.False(_modules.Claims(4242));
    }

    [Fact]
    public void A_plugins_page_goes_when_it_is_unloaded()
    {
        var events = Events();
        var host = Host(events);

        host.LoadFromInstance("police", new PagePlugin());

        Assert.Equal(new[] { "police" }, _panel.Pages);

        host.Unload("police");

        Assert.Empty(_panel.Pages);
    }

    [Fact]
    public void A_loaded_plugin_is_started_and_its_handlers_take_effect()
    {
        var events = Events();
        var host = Host(events);
        var spy = new Spy();

        host.LoadFromInstance("police", spy);

        Assert.True(spy.Started);
        Assert.False(events.Spawn.Raise(
            new SpawnEvent(1, "a", PermissionLevel.Default, "b", 1)).Allowed);
    }

    [Fact]
    public void Unloading_stops_the_plugin_and_detaches_its_handlers()
    {
        var events = Events();
        var host = Host(events);
        var spy = new Spy();

        host.LoadFromInstance("police", spy);

        Assert.True(host.Unload("police"));
        Assert.True(spy.Stopped);
        Assert.True(events.Spawn.Raise(
            new SpawnEvent(1, "a", PermissionLevel.Default, "b", 1)).Allowed);
    }

    [Fact]
    public void Handlers_are_detached_by_the_host_even_if_the_plugin_forgets()
    {
        // The plugin above never unsubscribes. The host does it, so a forgetful
        // plugin cannot leave a handler running against an assembly that has gone.
        var events = Events();
        var host = Host(events);

        host.LoadFromInstance("police", new Spy());
        host.Unload("police");

        Assert.Equal(0, events.Spawn.Count);
    }

    [Fact]
    public void A_plugin_that_throws_in_start_is_not_loaded()
    {
        var events = Events();
        var host = Host(events);

        host.LoadFromInstance("broken", new Spy { ThrowOnStart = true });

        Assert.Empty(host.Loaded);
        Assert.Contains(_log, line => line.Contains("broken"));
    }

    [Fact]
    public void A_plugin_that_throws_in_start_leaves_no_handlers_behind()
    {
        var events = Events();
        var host = Host(events);

        host.LoadFromInstance("broken", new Spy { ThrowOnStart = true });

        Assert.Equal(0, events.Spawn.Count);
    }

    [Fact]
    public void A_plugin_that_throws_on_shutdown_is_still_unloaded()
    {
        var events = Events();
        var host = Host(events);

        host.LoadFromInstance("rude", new RudeShutdown());

        Assert.True(host.Unload("rude"));
        Assert.Empty(host.Loaded);
    }

    [Fact]
    public void Unloading_something_that_is_not_loaded_says_so()
    {
        Assert.False(Host(Events()).Unload("nothing"));
    }

    [Fact]
    public void Loading_an_empty_directory_loads_nothing_and_does_not_throw()
    {
        Assert.Equal(0, Host(Events()).LoadAll());
    }

    [Fact]
    public void A_directory_with_no_manifest_is_skipped_with_a_reason()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "half-copied"));

        Assert.Equal(0, Host(Events()).LoadAll());
        Assert.Contains(_log, line => line.Contains("half-copied"));
    }

    [Fact]
    public void A_manifest_for_the_wrong_api_version_is_skipped_with_both_numbers()
    {
        string folder = Path.Combine(_dir, "old");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"),
            """{ "name": "old", "version": "1", "apiVersion": 99, "entry": "Old.dll" }""");

        Assert.Equal(0, Host(Events()).LoadAll());
        Assert.Contains(_log, line => line.Contains("99"));
    }

    [Fact]
    public void Reloading_gives_a_disabled_plugin_a_fresh_start()
    {
        var events = Events();
        var host = Host(events);

        _health.NoteFailure("police");
        _health.NoteFailure("police");
        _health.NoteFailure("police");

        host.LoadFromInstance("police", new Spy());
        host.Unload("police");

        Assert.False(_health.IsDisabled("police"));
    }
}
