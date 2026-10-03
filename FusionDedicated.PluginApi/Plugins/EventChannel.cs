namespace FusionDedicated.Plugins;

/// <summary>
/// One event that plugins can watch and refuse. Handlers run in the order they
/// subscribed. Raise stops at the first refusal, so a refusal costs nothing after
/// it and the log names one plugin rather than several. Notify is for news that
/// cannot be refused, and reaches every subscriber.
///
/// A handler that throws allows the event. Refusing on a crash would be
/// indistinguishable from the server itself blocking everything, which is the
/// hardest kind of fault to trace.
/// </summary>
public sealed class EventChannel<T>
{
    private readonly record struct Subscription(string Plugin, Func<T, PluginVerdict> Handler);

    private readonly List<Subscription> _subscriptions = new();
    private readonly PluginHealth _health;
    private readonly Action<string, string> _log;
    private readonly object _lock = new();

    public EventChannel(PluginHealth health, Action<string, string> log)
    {
        _health = health;
        _log = log;
    }

    public int Count
    {
        get { lock (_lock) { return _subscriptions.Count; } }
    }

    public void Subscribe(string plugin, Func<T, PluginVerdict> handler)
    {
        lock (_lock)
        {
            _subscriptions.Add(new Subscription(plugin, handler));
        }
    }

    /// <summary>Detaches everything a plugin registered, for unload or reload.</summary>
    public void RemoveAll(string plugin)
    {
        lock (_lock)
        {
            _subscriptions.RemoveAll(s =>
                string.Equals(s.Plugin, plugin, StringComparison.OrdinalIgnoreCase));
        }
    }

    public PluginVerdict Raise(T payload) => Run(payload, stopAtRefusal: true);

    /// <summary>For news of something already done: every handler hears it, whatever the ones before it said.</summary>
    public void Notify(T payload) => Run(payload, stopAtRefusal: false);

    private PluginVerdict Run(T payload, bool stopAtRefusal)
    {
        List<Subscription> current;

        // Copied so a handler that loads or unloads a plugin cannot change the list
        // being walked.
        lock (_lock)
        {
            current = new List<Subscription>(_subscriptions);
        }

        foreach (var subscription in current)
        {
            if (_health.IsDisabled(subscription.Plugin))
            {
                continue;
            }

            PluginVerdict verdict;

            try
            {
                verdict = subscription.Handler(payload) ?? PluginVerdict.Allow;
            }
            catch (Exception e)
            {
                _log("WARN", $"Plugin '{subscription.Plugin}' threw: {e.Message}");

                if (_health.NoteFailure(subscription.Plugin))
                {
                    _log("ERROR", $"Plugin '{subscription.Plugin}' has thrown "
                                + $"{PluginHealth.FailuresBeforeDisable} times and is disabled");
                }

                continue;
            }

            if (stopAtRefusal && !verdict.Allowed)
            {
                return verdict;
            }
        }

        return PluginVerdict.Allow;
    }
}
