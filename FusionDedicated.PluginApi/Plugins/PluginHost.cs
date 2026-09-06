using System.Runtime.Loader;

namespace FusionDedicated.Plugins;

/// <summary>
/// A plugin the server is holding, together with the context it was loaded into.
/// The load context is null for a plugin handed over as an instance, which is how
/// the tests exercise the lifecycle without a second assembly on disk.
/// </summary>
public sealed class LoadedPlugin
{
    public required string Name { get; init; }

    public required PluginManifest Manifest { get; init; }

    public required IFusionPlugin Instance { get; init; }

    public required PluginStore Store { get; init; }

    public AssemblyLoadContext? Context { get; init; }
}

/// <summary>
/// Finds, loads and unloads plugins.
///
/// Each plugin gets a collectible load context of its own so it can be replaced
/// without restarting the server. The host detaches a plugin's event handlers
/// itself rather than trusting the plugin to do it, because a handler left
/// pointing at an unloaded assembly is the failure that makes collectible
/// contexts unpleasant.
/// </summary>
public sealed class PluginHost
{
    private readonly string _directory;
    private readonly PluginEvents _events;
    private readonly PluginHealth _health;
    private readonly PluginPanel _panel;
    private readonly IPluginActions _actions;
    private readonly Action<string, string> _log;

    private readonly List<LoadedPlugin> _loaded = new();
    private readonly object _lock = new();

    public PluginHost(string directory, PluginEvents events, PluginHealth health,
        PluginPanel panel, IPluginActions actions, Action<string, string> log)
    {
        _directory = directory;
        _events = events;
        _health = health;
        _panel = panel;
        _actions = actions;
        _log = log;
    }

    public IReadOnlyList<LoadedPlugin> Loaded
    {
        get { lock (_lock) { return _loaded.ToList(); } }
    }

    /// <summary>Loads every plugin directory. Returns how many started.</summary>
    public int LoadAll()
    {
        if (!Directory.Exists(_directory))
        {
            return 0;
        }

        var started = 0;

        foreach (string folder in Directory.GetDirectories(_directory))
        {
            if (LoadFolder(folder))
            {
                started++;
            }
        }

        return started;
    }

    /// <summary>Unloads everything and loads it again. Returns how many started.</summary>
    public int ReloadAll()
    {
        foreach (var plugin in Loaded)
        {
            Unload(plugin.Name);
        }

        return LoadAll();
    }

    private bool LoadFolder(string folder)
    {
        string name = Path.GetFileName(folder);
        string manifestPath = Path.Combine(folder, "plugin.json");

        if (!File.Exists(manifestPath))
        {
            _log("WARN", $"Plugin '{name}' has no plugin.json, so it was skipped");
            return false;
        }

        var manifest = PluginManifest.TryParse(File.ReadAllText(manifestPath), out string error);

        if (manifest == null)
        {
            _log("WARN", $"Plugin '{name}' was skipped: {error}");
            return false;
        }

        string assemblyPath = Path.Combine(folder, manifest.Entry);

        if (!File.Exists(assemblyPath))
        {
            _log("WARN", $"Plugin '{manifest.Name}' names {manifest.Entry}, which is not there");
            return false;
        }

        var context = new AssemblyLoadContext($"plugin:{manifest.Name}", isCollectible: true);

        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

            var type = assembly.GetTypes().FirstOrDefault(t =>
                typeof(IFusionPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

            if (type == null)
            {
                _log("WARN", $"Plugin '{manifest.Name}' has no class implementing IFusionPlugin");
                context.Unload();
                return false;
            }

            if (Activator.CreateInstance(type) is not IFusionPlugin instance)
            {
                _log("WARN", $"Plugin '{manifest.Name}' could not be created");
                context.Unload();
                return false;
            }

            return Start(manifest, instance, context, folder);
        }
        catch (Exception e)
        {
            _log("ERROR", $"Plugin '{manifest.Name}' failed to load: {e.Message}");
            context.Unload();
            return false;
        }
    }

    /// <summary>Starts a plugin the caller already has, rather than one on disk.</summary>
    public bool LoadFromInstance(string name, IFusionPlugin instance)
    {
        var manifest = new PluginManifest
        {
            Name = name,
            Version = "test",
            ApiVersion = PluginManifest.CurrentApiVersion,
            Entry = "none",
        };

        return Start(manifest, instance, null, Path.Combine(_directory, name));
    }

    private bool Start(PluginManifest manifest, IFusionPlugin instance,
        AssemblyLoadContext? context, string folder)
    {
        var store = new PluginStore(Path.Combine(folder, "data.json"));
        store.Load();

        var pluginContext = new PluginContext(
            manifest.Name, _events, store, _panel, _actions, _log);

        try
        {
            instance.Start(pluginContext);
        }
        catch (Exception e)
        {
            // Anything it managed to register before throwing has to go, or a plugin
            // that failed to start would still be answering events.
            _events.RemoveAll(manifest.Name);
            _panel.RemoveAll(manifest.Name);
            _log("ERROR", $"Plugin '{manifest.Name}' threw while starting and was not loaded: {e.Message}");
            context?.Unload();
            return false;
        }

        lock (_lock)
        {
            _loaded.Add(new LoadedPlugin
            {
                Name = manifest.Name,
                Manifest = manifest,
                Instance = instance,
                Store = store,
                Context = context,
            });
        }

        _log("INFO", $"Plugin '{manifest.Name}' {manifest.Version} loaded");
        return true;
    }

    public bool Unload(string name)
    {
        LoadedPlugin? plugin;

        lock (_lock)
        {
            plugin = _loaded.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (plugin == null)
            {
                return false;
            }

            _loaded.Remove(plugin);
        }

        try
        {
            plugin.Instance.Shutdown();
        }
        catch (Exception e)
        {
            // Its own fault, but it is going anyway.
            _log("WARN", $"Plugin '{plugin.Name}' threw while shutting down: {e.Message}");
        }

        _events.RemoveAll(plugin.Name);
        _panel.RemoveAll(plugin.Name);
        _health.Forget(plugin.Name);
        plugin.Store.Save();
        plugin.Context?.Unload();

        _log("INFO", $"Plugin '{plugin.Name}' unloaded");
        return true;
    }
}
