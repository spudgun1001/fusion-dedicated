namespace FusionDedicated.Plugins;

/// <summary>What one plugin is asking another to do.</summary>
public readonly record struct BusRequest(string From, string Verb, IReadOnlyDictionary<string, string> Args)
{
    public string Arg(string key) => Args.GetValueOrDefault(key, "");

    public long Number(string key, long fallback = 0)
        => long.TryParse(Arg(key), out long value) ? value : fallback;

    public ulong PlayerId(string key)
        => ulong.TryParse(Arg(key), out ulong value) ? value : 0UL;
}

/// <summary>The answer, and whether anybody was there to give one.</summary>
/// <param name="Offered">False when no plugin offers that verb, which is not a failure.</param>
public readonly record struct BusReply(bool Offered, bool Ok, string Error, string Value)
{
    /// <summary>Nobody offers it. The asker carries on without whatever it wanted.</summary>
    public static readonly BusReply Unoffered = new(false, false, "", "");

    public static BusReply Yes(string value = "") => new(true, true, "", value);

    public static BusReply No(string why) => new(true, false, why, "");
}

/// <summary>
/// One plugin asking another for something.
///
/// Plugins are separate assemblies in separate load contexts, so they cannot call
/// each other and cannot share a type. They can still agree on a verb and some
/// strings, which is enough for the things they actually want: the phones plugin
/// charging a call to somebody's LabRP balance, without either knowing the other
/// exists at compile time.
///
/// An unoffered verb is answered rather than thrown. A server running phones and
/// no economy should have working phones that do not bill, not a plugin that
/// falls over on the first call.
/// </summary>
public sealed class PluginBus
{
    private readonly Dictionary<(string Plugin, string Verb), Func<BusRequest, BusReply>> _offers = new();
    private readonly PluginHealth _health;
    private readonly Action<string, string> _log;
    private readonly object _lock = new();

    public PluginBus(PluginHealth health, Action<string, string> log)
    {
        _health = health;
        _log = log;
    }

    /// <summary>Says this plugin will answer a verb.</summary>
    public void Offer(string plugin, string verb, Func<BusRequest, BusReply> handler)
    {
        lock (_lock)
        {
            _offers[(plugin, Tidy(verb))] = handler;
        }
    }

    public bool Offers(string plugin, string verb)
    {
        lock (_lock)
        {
            return _offers.ContainsKey((plugin, Tidy(verb)));
        }
    }

    public void RemoveAll(string plugin)
    {
        lock (_lock)
        {
            foreach (var key in _offers.Keys.Where(k => k.Plugin == plugin).ToList())
            {
                _offers.Remove(key);
            }
        }
    }

    /// <summary>Asks a named plugin for something.</summary>
    public BusReply Ask(string from, string plugin, string verb, IReadOnlyDictionary<string, string>? args = null)
    {
        Func<BusRequest, BusReply>? handler;

        lock (_lock)
        {
            _offers.TryGetValue((plugin, Tidy(verb)), out handler);
        }

        if (handler == null)
        {
            return BusReply.Unoffered;
        }

        try
        {
            return handler(new BusRequest(from, Tidy(verb),
                args ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        }
        catch (Exception e)
        {
            _log("WARN", $"Plugin '{plugin}' threw answering '{verb}' for '{from}': {e.Message}");

            if (_health.NoteFailure(plugin))
            {
                _log("WARN", $"Plugin '{plugin}' has been disabled after too many faults");
            }

            return BusReply.No("that plugin failed");
        }
    }

    private static string Tidy(string verb) => (verb ?? "").Trim().ToLowerInvariant();
}
