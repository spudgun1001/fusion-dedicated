using System.Text.Json;
using FusionDedicated.Web;

namespace FusionDedicated.Plugins;

public sealed record PluginHttpRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query, string Body, string Actor);

public readonly record struct PluginHttpReply(int Status, string Json)
{
    public static PluginHttpReply Ok(string json) => new(200, json);

    public static PluginHttpReply Error(int status, string message)
        => new(status, JsonSerializer.Serialize(new { error = message }));
}

/// <summary>JSON routes plugins answer under /api/plugins/http/{plugin}/{path}, each open to one panel role.</summary>
public sealed class PluginHttp
{
    private const char KeySeparator = '\0';

    private readonly Dictionary<string, (PanelRole Role, Func<PluginHttpRequest, PluginHttpReply> Handler)> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly PluginHealth _health;
    private readonly Action<string, string> _log;
    private readonly object _lock = new();

    public PluginHttp(PluginHealth health, Action<string, string> log)
    {
        _health = health;
        _log = log;
    }

    public void Handle(string plugin, string path, PanelRole role, Func<PluginHttpRequest, PluginHttpReply> handler)
    {
        if (string.IsNullOrWhiteSpace(plugin)) throw new ArgumentException("A route needs a plugin name", nameof(plugin));
        if (path == null) throw new ArgumentException("A route needs a path", nameof(path));
        if (handler == null) throw new ArgumentException("A route needs a handler", nameof(handler));

        lock (_lock)
        {
            _routes[Key(plugin, path)] = (role, handler);
        }
    }

    public PanelRole? RoleFor(string plugin, string path) => Find(plugin, path)?.Role;

    public PluginHttpReply Invoke(string plugin, string path, PluginHttpRequest request)
    {
        var handler = Find(plugin, path)?.Handler;

        if (handler == null) return PluginHttpReply.Error(404, "No such route");

        try
        {
            return handler(request);
        }
        catch (Exception e)
        {
            _log("WARN", $"Plugin '{plugin}' threw answering '{path}': {e.Message}");
            if (_health.NoteFailure(plugin))
            {
                _log("ERROR", $"Plugin '{plugin}' has thrown {PluginHealth.FailuresBeforeDisable} times and is disabled");
            }

            return PluginHttpReply.Error(500, "The plugin failed");
        }
    }

    /// <summary>Takes a plugin's routes away, for unload or reload.</summary>
    public void RemoveAll(string plugin)
    {
        lock (_lock)
        {
            foreach (string key in _routes.Keys.Where(k => k.StartsWith(plugin + KeySeparator, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _routes.Remove(key);
            }
        }
    }

    private (PanelRole Role, Func<PluginHttpRequest, PluginHttpReply> Handler)? Find(string plugin, string path)
    {
        if (_health.IsDisabled(plugin)) return null;

        lock (_lock)
        {
            return _routes.TryGetValue(Key(plugin, path), out var route) ? route : null;
        }
    }

    private static string Key(string plugin, string path) => plugin + KeySeparator + (path ?? "").Trim('/');
}
