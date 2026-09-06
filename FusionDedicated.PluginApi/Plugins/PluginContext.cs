namespace FusionDedicated.Plugins;

/// <summary>
/// Everything a plugin is given. Passed to Start once, and the only way in.
/// </summary>
public sealed class PluginContext
{
    private readonly Action<string, string> _log;
    private readonly Func<IReadOnlyList<PluginPlayer>> _players;

    public PluginContext(string name, PluginEvents events, PluginStore store,
        PluginPanel panel, PluginModules modules, IPluginActions actions,
        Func<IReadOnlyList<PluginPlayer>> players, Action<string, string> log)
    {
        _players = players;
        Name = name;
        Events = events;
        Store = store;
        Panel = panel;
        Modules = modules;
        Actions = actions;
        _log = log;
    }

    public string Name { get; }

    public PluginEvents Events { get; }

    public PluginStore Store { get; }

    /// <summary>The page this plugin offers in the web panel, if it wants one.</summary>
    public PluginPanel Panel { get; }

    /// <summary>Module message tags this plugin has claimed, if any.</summary>
    public PluginModules Modules { get; }

    /// <summary>Who is connected, read afresh each time it is asked for.</summary>
    public IReadOnlyList<PluginPlayer> Players => _players();

    public IPluginActions Actions { get; }

    /// <summary>Writes to the server log, marked with the plugin's name.</summary>
    public void Log(string level, string message) => _log(level, $"[{Name}] {message}");
}
