using FusionDedicated.Web;

namespace FusionDedicated.Plugins;

/// <summary>Whether a button did anything, and why not when it did not.</summary>
public readonly record struct PanelActionResult(bool Handled, string Error)
{
    public static readonly PanelActionResult Done = new(true, "");

    public static PanelActionResult Failed(string error) => new(false, error);
}

/// <summary>
/// The pages plugins offer and the buttons on them. A page is built on every
/// request rather than stored, so the panel always shows what the plugin holds
/// now without the plugin having to push anything.
/// </summary>
public sealed class PluginPanel
{
    private const char KeySeparator = '\0';

    private readonly Dictionary<string, Func<PluginViewer, PluginPage>> _pages =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Who a page is built for when the caller does not say.</summary>
    private static readonly PluginViewer Owner = new("panel", PanelRole.Owner);

    private readonly Dictionary<string, Action<IReadOnlyDictionary<string, string>>> _actions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly PluginHealth _health;
    private readonly Action<string, string> _log;
    private readonly object _lock = new();

    public PluginPanel(PluginHealth health, Action<string, string> log)
    {
        _health = health;
        _log = log;
    }

    public IReadOnlyList<string> Pages
    {
        get { lock (_lock) { return _pages.Keys.ToList(); } }
    }

    public void Register(string plugin, Func<PluginPage> build)
        => Register(plugin, _ => build());

    /// <summary>Registers a page that is built for the panel account looking at it.</summary>
    public void Register(string plugin, Func<PluginViewer, PluginPage> build)
    {
        lock (_lock)
        {
            _pages[plugin] = build;
        }
    }

    public void OnAction(string plugin, string action,
        Action<IReadOnlyDictionary<string, string>> handler)
    {
        lock (_lock)
        {
            _actions[Key(plugin, action)] = handler;
        }
    }

    public PluginPage? Build(string plugin) => Build(plugin, Owner);

    public PluginPage? Build(string plugin, PluginViewer viewer)
    {
        if (_health.IsDisabled(plugin))
        {
            return null;
        }

        Func<PluginViewer, PluginPage>? build;

        lock (_lock)
        {
            if (!_pages.TryGetValue(plugin, out build))
            {
                return null;
            }
        }

        try
        {
            return build(viewer);
        }
        catch (Exception e)
        {
            Note(plugin, $"threw building its page: {e.Message}");
            return null;
        }
    }

    public PanelActionResult Invoke(string plugin, string action,
        IReadOnlyDictionary<string, string> values)
    {
        if (_health.IsDisabled(plugin))
        {
            return PanelActionResult.Failed($"'{plugin}' is disabled");
        }

        Action<IReadOnlyDictionary<string, string>>? handler;

        lock (_lock)
        {
            if (!_actions.TryGetValue(Key(plugin, action), out handler))
            {
                return PanelActionResult.Failed($"'{plugin}' has no action called '{action}'");
            }
        }

        try
        {
            handler(values);
            return PanelActionResult.Done;
        }
        catch (Exception e)
        {
            Note(plugin, $"threw handling '{action}': {e.Message}");
            return PanelActionResult.Failed(e.Message);
        }
    }

    /// <summary>Takes a plugin's page and buttons away, for unload or reload.</summary>
    public void RemoveAll(string plugin)
    {
        lock (_lock)
        {
            _pages.Remove(plugin);

            foreach (string key in _actions.Keys
                .Where(k => k.StartsWith(plugin + KeySeparator, StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                _actions.Remove(key);
            }
        }
    }

    private void Note(string plugin, string what)
    {
        _log("WARN", $"Plugin '{plugin}' {what}");

        if (_health.NoteFailure(plugin))
        {
            _log("ERROR", $"Plugin '{plugin}' has thrown "
                        + $"{PluginHealth.FailuresBeforeDisable} times and is disabled");
        }
    }

    private static string Key(string plugin, string action) => plugin + KeySeparator + action;
}
