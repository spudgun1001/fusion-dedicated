using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Plugins;

/// <summary>What a plugin should do about an RPC it was shown.</summary>
public enum RpcActionKind
{
    /// <summary>Pass it on to the other clients, which is what would have happened anyway.</summary>
    Pass,

    /// <summary>Keep it. Use when the plugin is answering rather than watching.</summary>
    Drop,
}

public readonly record struct RpcAction(RpcActionKind Kind)
{
    public static readonly RpcAction Pass = new(RpcActionKind.Pass);

    public static readonly RpcAction Drop = new(RpcActionKind.Drop);
}

/// <summary>One RPC a client sent, as a plugin sees it.</summary>
/// <param name="Path">
/// Which component. Opaque and stable, so it is the key to hold on to: a plugin
/// learns a prop by seeing it speak, then talks back to the same path.
/// </param>
/// <param name="EntityId">Zero when the component came with the level rather than a spawn.</param>
public readonly record struct RpcRequest(
    ulong PlatformId,
    string Name,
    PermissionLevel Rank,
    RpcKind Kind,
    string Path,
    bool HasEntity,
    ushort EntityId,
    ushort ComponentIndex,
    RpcValue Value);

/// <summary>
/// The RPC components of a Marrow pallet, opened up to plugins.
///
/// A pallet on mod.io cannot ship code, and the server cannot run the game, so
/// these components are the only thing the two can both speak. An author drops an
/// RPCInt or an RPCEvent on a prefab, wires it in the inspector, and a plugin on
/// the server can read it and write it back.
///
/// A plugin watches everything rather than registering a path, because it has no
/// way to know a path before it has seen one: they are hashes of a prefab's place
/// in a level, not names. The usual shape is to wait for a prop to announce
/// itself, remember its path, and answer that.
/// </summary>
public sealed class PluginRpc
{
    private readonly Dictionary<string, Func<RpcRequest, RpcAction>> _watchers = new(StringComparer.Ordinal);
    private readonly PluginHealth _health;
    private readonly Action<string, string> _log;
    private readonly object _lock = new();

    public PluginRpc(PluginHealth health, Action<string, string> log)
    {
        _health = health;
        _log = log;
    }

    /// <summary>
    /// How the server sends one. Set by the host, and null when plugins are
    /// loaded outside a server, which is how the tests exercise this.
    /// </summary>
    /// <remarks>
    /// Arguments are the kind, the path, the value, and who to send it to, where
    /// null means everybody.
    /// </remarks>
    public Action<RpcKind, string, RpcValue, ulong?>? Sender { get; set; }

    public bool Watched
    {
        get { lock (_lock) { return _watchers.Count > 0; } }
    }

    /// <summary>Shows a plugin every RPC that passes through.</summary>
    public void Watch(string plugin, Func<RpcRequest, RpcAction> handler)
    {
        lock (_lock)
        {
            _watchers[plugin] = handler;
        }
    }

    public void RemoveAll(string plugin)
    {
        lock (_lock)
        {
            _watchers.Remove(plugin);
        }
    }

    /// <summary>
    /// Offers one to every watcher. The first that says Drop wins, and the rest
    /// are still shown it, because watching is the common case and one plugin
    /// answering should not blind another.
    /// </summary>
    public RpcAction Dispatch(RpcRequest request)
    {
        List<KeyValuePair<string, Func<RpcRequest, RpcAction>>> watchers;

        lock (_lock)
        {
            watchers = _watchers.ToList();
        }

        var result = RpcAction.Pass;

        foreach (var (plugin, handler) in watchers)
        {
            try
            {
                if (handler(request).Kind == RpcActionKind.Drop)
                {
                    result = RpcAction.Drop;
                }
            }
            catch (Exception e)
            {
                _log("WARN", $"Plugin '{plugin}' threw on an RPC: {e.Message}");

                if (_health.NoteFailure(plugin))
                {
                    _log("WARN", $"Plugin '{plugin}' has been disabled after too many faults");
                }
            }
        }

        return result;
    }

    /// <summary>Sets a value on a component, for everybody or for one player.</summary>
    /// <remarks>A broadcast of the value the server already holds is skipped, so undo a per-player value with another per-player send.</remarks>
    public void SetInt(string path, int value, ulong? platformId = null)
        => Send(RpcKind.Int, path, RpcValue.OfInt(value), platformId);

    public void SetFloat(string path, float value, ulong? platformId = null)
        => Send(RpcKind.Float, path, RpcValue.OfFloat(value), platformId);

    public void SetBool(string path, bool value, ulong? platformId = null)
        => Send(RpcKind.Bool, path, RpcValue.OfBool(value), platformId);

    public void SetString(string path, string value, ulong? platformId = null)
        => Send(RpcKind.String, path, RpcValue.OfString(value), platformId);

    public void SetVector(string path, float x, float y, float z, ulong? platformId = null)
        => Send(RpcKind.Vector3, path, RpcValue.OfVector(x, y, z), platformId);

    /// <summary>Fires a one-shot on a component.</summary>
    public void FireEvent(string path, ulong? platformId = null)
        => Send(RpcKind.Event, path, RpcValue.Nothing, platformId);

    private void Send(RpcKind kind, string path, RpcValue value, ulong? platformId)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Sender?.Invoke(kind, path, value, platformId);
    }
}
