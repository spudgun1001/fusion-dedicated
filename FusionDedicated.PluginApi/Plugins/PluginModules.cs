namespace FusionDedicated.Plugins;

/// <summary>
/// Module message tags plugins have claimed. This is what makes support for a
/// client mod a plugin rather than a change to the server: a plugin computes the
/// mod's handler tag with <c>ModuleProtocol.TagFor</c> and takes it.
///
/// One handler per tag. A second plugin asking for a tag already claimed is
/// refused out loud, because two plugins rewriting one message would fight and
/// the loser would never know.
/// </summary>
public sealed class PluginModules
{
    private readonly record struct Claim(string Plugin, Func<ModuleRequest, ModuleAction> Handler);

    private readonly Dictionary<long, Claim> _claims = new();
    private readonly PluginHealth _health;
    private readonly Action<string, string> _log;
    private readonly object _lock = new();

    public PluginModules(PluginHealth health, Action<string, string> log)
    {
        _health = health;
        _log = log;
    }

    public void Handle(string plugin, long handlerTag, Func<ModuleRequest, ModuleAction> handler)
    {
        lock (_lock)
        {
            if (_claims.TryGetValue(handlerTag, out var existing))
            {
                _log("WARN", $"Plugin '{plugin}' asked for module tag {handlerTag}, "
                           + $"which '{existing.Plugin}' already has, so it was refused");
                return;
            }

            _claims[handlerTag] = new Claim(plugin, handler);
        }
    }

    public bool Claims(long handlerTag)
    {
        lock (_lock)
        {
            return _claims.ContainsKey(handlerTag);
        }
    }

    public ModuleAction Dispatch(ModuleRequest request)
    {
        Claim claim;

        lock (_lock)
        {
            if (!_claims.TryGetValue(request.HandlerTag, out claim))
            {
                return ModuleAction.Forward;
            }
        }

        if (_health.IsDisabled(claim.Plugin))
        {
            return ModuleAction.Forward;
        }

        try
        {
            return claim.Handler(request) ?? ModuleAction.Forward;
        }
        catch (Exception e)
        {
            _log("WARN", $"Plugin '{claim.Plugin}' threw on module tag "
                       + $"{request.HandlerTag}: {e.Message}");

            if (_health.NoteFailure(claim.Plugin))
            {
                _log("ERROR", $"Plugin '{claim.Plugin}' has thrown "
                            + $"{PluginHealth.FailuresBeforeDisable} times and is disabled");
            }

            // Forwarding leaves the server behaving as though the plugin were not
            // there, rather than swallowing traffic a client is waiting on.
            return ModuleAction.Forward;
        }
    }

    /// <summary>Frees every tag a plugin claimed, for unload or reload.</summary>
    public void RemoveAll(string plugin)
    {
        lock (_lock)
        {
            foreach (long tag in _claims
                .Where(c => string.Equals(c.Value.Plugin, plugin, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Key)
                .ToList())
            {
                _claims.Remove(tag);
            }
        }
    }
}
