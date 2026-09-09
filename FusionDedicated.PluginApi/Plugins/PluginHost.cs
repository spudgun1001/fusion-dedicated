using System.Reflection;
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

    /// <summary>What the plugin was handed, kept so its timers can be stopped.</summary>
    public PluginContext? Given { get; init; }
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
    private readonly string _dataDirectory;
    private readonly PluginEvents _events;
    private readonly PluginHealth _health;
    private readonly PluginPanel _panel;
    private readonly PluginModules _modules;
    private readonly PluginRpc _rpc = null!;
    private readonly PluginBus _bus = null!;
    private readonly PluginWorld _world = null!;
    private readonly Func<IReadOnlyList<PluginPlayer>> _players;
    private readonly IPluginActions _actions;
    private readonly Action<string, string> _log;

    private readonly List<LoadedPlugin> _loaded = new();
    private readonly object _lock = new();

    public PluginHost(string directory, PluginEvents events, PluginHealth health,
        PluginPanel panel, PluginModules modules, IPluginActions actions,
        Func<IReadOnlyList<PluginPlayer>> players, Action<string, string> log)
        : this(directory,
            Path.GetFullPath(Path.Combine(directory, "..", "plugin-data")),
            events, health, panel, modules, actions, players, log)
    {
    }

    /// <summary>The one the server uses, which also passes the RPC surface through.</summary>
    public PluginHost(string directory, string dataDirectory, PluginEvents events,
        PluginHealth health, PluginPanel panel, PluginModules modules, PluginRpc rpc,
        PluginBus bus, PluginWorld world, IPluginActions actions,
        Func<IReadOnlyList<PluginPlayer>> players, Action<string, string> log)
        : this(directory, dataDirectory, events, health, panel, modules, actions, players, log)
    {
        _rpc = rpc;
        _bus = bus;
        _world = world;
    }

    /// <param name="dataDirectory">
    /// Where a plugin's saved state is kept.
    ///
    /// Deliberately outside the plugin folder. Replacing a plugin means replacing
    /// that folder, which took everybody's balances with it, and a folder uploaded
    /// through a panel is often not writable by the account the server runs as, so
    /// nothing could be saved at all.
    /// </param>
    public PluginHost(string directory, string dataDirectory, PluginEvents events,
        PluginHealth health, PluginPanel panel, PluginModules modules, IPluginActions actions,
        Func<IReadOnlyList<PluginPlayer>> players, Action<string, string> log)
    {
        _players = players;
        _directory = directory;
        _dataDirectory = dataDirectory;
        _events = events;
        _health = health;
        _panel = panel;
        _modules = modules;
        _actions = actions;
        _log = log;
        _rpc ??= new PluginRpc(health, log);
        _bus ??= new PluginBus(health, log);
        _world ??= new PluginWorld();
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
    /// <summary>
    /// Unloads everything and reads the folder again.
    ///
    /// One plugin refusing to unload must not stop the rest being reloaded, or a
    /// single bad plugin leaves the server running yesterday's code with no sign
    /// of it beyond one line in the log.
    /// </summary>
    public int ReloadAll()
    {
        foreach (var plugin in Loaded)
        {
            try
            {
                Unload(plugin.Name);
            }
            catch (Exception e)
            {
                _log("WARN", $"Plugin '{plugin.Name}' could not be unloaded: {e.Message}");
            }
        }

        return LoadAll();
    }

    /// <summary>
    /// Loads an assembly from its bytes, with its symbols when they are beside it
    /// so a stack trace out of a plugin still names lines.
    /// </summary>
    private static Assembly LoadFrom(AssemblyLoadContext context, string path)
    {
        using var assembly = new MemoryStream(File.ReadAllBytes(path));

        string symbols = Path.ChangeExtension(path, ".pdb");

        if (!File.Exists(symbols))
        {
            return context.LoadFromStream(assembly);
        }

        using var pdb = new MemoryStream(File.ReadAllBytes(symbols));

        return context.LoadFromStream(assembly, pdb);
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
            // Read, not mapped.
            //
            // LoadFromAssemblyPath holds the file open for as long as the plugin
            // is loaded, so uploading a new build over a running one fails or
            // half-writes, and a reload then loads the file that is still there.
            // That is a plugin that will not update however many times you reload
            // it, with nothing in the log to say so. Reading the bytes once means
            // the file is free the moment it has been read.
            var assembly = LoadFrom(context, Path.GetFullPath(assemblyPath));

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

    /// <summary>
    /// Where a plugin's state lives, moving it out of the plugin folder the first
    /// time this runs.
    ///
    /// Copied rather than moved: the old folder is often the very thing that could
    /// not be written to, and refusing to migrate because the source is read only
    /// would leave the data stranded where it already is. The old file is left
    /// alone and never read again once the new one exists.
    /// </summary>
    private string DataPathFor(string name, string folder)
    {
        string moved = Path.Combine(_dataDirectory, name + ".json");
        string original = Path.Combine(folder, "data.json");

        if (File.Exists(moved) || !File.Exists(original))
        {
            return moved;
        }

        try
        {
            Directory.CreateDirectory(_dataDirectory);
            File.Copy(original, moved);

            _log("INFO", $"Moved {name}'s saved data to '{moved}'. " +
                         "It now survives the plugin being replaced.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or NotSupportedException)
        {
            _log("WARN", $"{name}'s saved data could not be moved out of its plugin " +
                         $"folder ({e.Message}). Starting from what is there now.");

            return original;
        }

        return moved;
    }

    private bool Start(PluginManifest manifest, IFusionPlugin instance,
        AssemblyLoadContext? context, string folder)
    {
        var store = new PluginStore(DataPathFor(manifest.Name, folder), _log);
        store.Load();

        var pluginContext = new PluginContext(
            manifest.Name, _events, store, _panel, _modules, _rpc, _bus, _world, _actions, _players, _log);

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
            _modules.RemoveAll(manifest.Name);
            _rpc.RemoveAll(manifest.Name);
            _bus.RemoveAll(manifest.Name);
            _log("ERROR", $"Plugin '{manifest.Name}' threw while starting and was not loaded: {e.Message}");
            pluginContext.StopTimers();
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
                Given = pluginContext,
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
        _modules.RemoveAll(plugin.Name);
        _rpc.RemoveAll(plugin.Name);
        _bus.RemoveAll(plugin.Name);
        _health.Forget(plugin.Name);

        // Everything from here has to happen even if one part of it fails. An
        // unwritable data.json used to throw out of here and abort the whole
        // reload, leaving the old assembly loaded and the new one never read.
        try
        {
            // Timers first. One left running would fire into an assembly that is
            // no longer loaded, which ends the process rather than throwing.
            plugin.Given?.StopTimers();
            plugin.Store.Save();
            plugin.Context?.Unload();
        }
        catch (Exception e)
        {
            _log("WARN", $"Plugin '{plugin.Name}' did not unload cleanly: {e.Message}");
        }

        _log("INFO", $"Plugin '{plugin.Name}' unloaded");
        return true;
    }
}
