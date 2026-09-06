namespace FusionDedicated.Plugins;

/// <summary>
/// Everything a plugin is given. Passed to Start once, and the only way in.
/// </summary>
public sealed class PluginContext
{
    private readonly Action<string, string> _log;

    public PluginContext(string name, PluginEvents events, PluginStore store,
        IPluginActions actions, Action<string, string> log)
    {
        Name = name;
        Events = events;
        Store = store;
        Actions = actions;
        _log = log;
    }

    public string Name { get; }

    public PluginEvents Events { get; }

    public PluginStore Store { get; }

    public IPluginActions Actions { get; }

    /// <summary>Writes to the server log, marked with the plugin's name.</summary>
    public void Log(string level, string message) => _log(level, $"[{Name}] {message}");
}
