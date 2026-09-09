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

    private int _running;
    private bool _stopped;

    public PluginContext(string name, PluginEvents events, PluginStore store,
        PluginPanel panel, PluginModules modules, IPluginActions actions,
        Func<IReadOnlyList<PluginPlayer>> players, Action<string, string> log)
        : this(name, events, store, panel, modules, new PluginRpc(new PluginHealth(), log),
            new PluginBus(new PluginHealth(), log), new PluginWorld(), actions, players, log)
    {
    }

    /// <summary>
    /// The one the server uses. Kept apart from the shorter form above so a
    /// plugin already compiled against that one still loads.
    /// </summary>
    public PluginContext(string name, PluginEvents events, PluginStore store,
        PluginPanel panel, PluginModules modules, PluginRpc rpc, PluginBus bus,
        PluginWorld world, IPluginActions actions, Func<IReadOnlyList<PluginPlayer>> players,
        Action<string, string> log)
    {
        Bus = bus;
        World = world;
        _players = players;
        Name = name;
        Events = events;
        Store = store;
        Panel = panel;
        Modules = modules;
        Rpc = rpc;
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

    /// <summary>
    /// The RPC components of whatever pallets are loaded, to read and to write.
    ///
    /// How a plugin talks to a prop built in Unity and shipped on mod.io, which
    /// cannot carry code of its own.
    /// </summary>
    public PluginRpc Rpc { get; }

    /// <summary>
    /// The other plugins on this server, by name and verb.
    ///
    /// Plugins cannot call each other, so they agree a verb and some strings. A
    /// verb nobody offers is answered rather than thrown, so a plugin that would
    /// like an economy still works on a server that has none.
    /// </summary>
    public PluginBus Bus { get; }

    /// <summary>
    /// What is in the world, to read: where a prop is, and whether it has been
    /// kept. A kept prop comes back after a restart at the same place under a new
    /// id, so a plugin that wants to remember something about one holds it against
    /// the place.
    /// </summary>
    public PluginWorld World { get; }

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
            // Counted so unloading can wait for it. Disposing a timer stops the
            // next callback being queued and does nothing about one already
            // running, so without this a payday could still be going while the
            // plugin was shut down and its store saved, and whatever it wrote
            // landed after the last save and was lost.
            Interlocked.Increment(ref _running);

            try
            {
                work();
            }
            catch (Exception e)
            {
                Log("WARN", $"A repeating job threw and was skipped: {e.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }, null, period, period);

        lock (_lock)
        {
            if (_stopped)
            {
                // Asked for after the plugin was told to stop. Nothing would
                // ever dispose it, so it is refused rather than left running.
                timer.Dispose();
                return;
            }

            _timers.Add(timer);
        }
    }

    /// <summary>
    /// Stops everything this plugin had running, and waits for whatever was
    /// already in flight to finish.
    ///
    /// Waiting matters. A job still running when the plugin is unloaded writes
    /// into a store that has already had its last save, so the work is lost, and
    /// it keeps the old assembly alive long enough that reloading can fail to
    /// read the file it is replacing.
    /// </summary>
    /// <param name="waitFor">How long to give it before going anyway.</param>
    public void StopTimers(TimeSpan? waitFor = null)
    {
        List<Timer> timers;

        lock (_lock)
        {
            _stopped = true;
            timers = _timers.ToList();
            _timers.Clear();
        }

        foreach (var timer in timers)
        {
            try { timer.Dispose(); } catch { }
        }

        var giveUpAt = DateTime.UtcNow + (waitFor ?? TimeSpan.FromSeconds(5));

        while (Volatile.Read(ref _running) > 0 && DateTime.UtcNow < giveUpAt)
        {
            Thread.Sleep(10);
        }

        if (Volatile.Read(ref _running) > 0)
        {
            Log("WARN", "A repeating job was still running and was left to finish. " +
                        "Anything it saves after this is lost.");
        }
    }
}
