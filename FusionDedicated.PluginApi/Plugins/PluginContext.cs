namespace FusionDedicated.Plugins;

/// <summary>
/// Everything a plugin is given. Passed to Start once, and the only way in.
/// </summary>
public sealed class PluginContext
{
    private readonly Action<string, string> _log;
    private readonly Func<IReadOnlyList<PluginPlayer>> _players;
    private readonly List<Timer> _timers = new();
    private readonly object _lock = new();

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

    /// <summary>
    /// Runs something on a repeat, safely.
    ///
    /// A plugin's own Timer hands its callback straight to the runtime, and an
    /// exception on a thread pool thread has nowhere to go: .NET ends the
    /// process. A LabRP payday did exactly that to a live server. Going through
    /// here means a throw is logged against the plugin instead.
    ///
    /// The timer is stopped when the plugin unloads, so nothing is left firing
    /// into an assembly that is no longer there.
    /// </summary>
    /// <param name="period">How often, and how long before the first run.</param>
    public void Every(TimeSpan period, Action work)
    {
        if (period < TimeSpan.FromSeconds(1))
        {
            period = TimeSpan.FromSeconds(1);
        }

        var timer = new Timer(_ =>
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                Log("WARN", $"A repeating job threw and was skipped: {e.Message}");
            }
        }, null, period, period);

        lock (_lock)
        {
            _timers.Add(timer);
        }
    }

    /// <summary>Stops everything this plugin had running. Called by the host.</summary>
    public void StopTimers()
    {
        List<Timer> timers;

        lock (_lock)
        {
            timers = _timers.ToList();
            _timers.Clear();
        }

        foreach (var timer in timers)
        {
            try { timer.Dispose(); } catch { }
        }
    }
}
