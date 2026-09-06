using System.Text.Json;

namespace FusionDedicated.Plugins;

/// <summary>
/// A plugin's own state, kept in one JSON file beside its DLL. The only
/// persistence offered, so plugin state is always somewhere known, backed up with
/// the server and editable by hand the way ranks.json is.
/// </summary>
public sealed class PluginStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly object _lock = new();
    private readonly Action<string, string>? _log;

    private Dictionary<string, JsonElement> _values = new(StringComparer.OrdinalIgnoreCase);
    private bool _warned;

    public PluginStore(string path, Action<string, string>? log = null)
    {
        _path = path;
        _log = log;
    }

    /// <summary>False once a write has failed and not yet succeeded again.</summary>
    public bool Writable { get; private set; } = true;

    public T? Get<T>(string key)
    {
        lock (_lock)
        {
            if (!_values.TryGetValue(key, out var element))
            {
                return default;
            }

            try
            {
                return element.Deserialize<T>(Options);
            }
            catch (JsonException)
            {
                return default;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        var element = JsonSerializer.SerializeToElement(value, Options);

        lock (_lock)
        {
            _values[key] = element;
        }
    }

    public bool Remove(string key)
    {
        lock (_lock)
        {
            return _values.Remove(key);
        }
    }

    /// <summary>Reads the file, keeping what is loaded if it will not parse.</summary>
    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                File.ReadAllText(_path), Options);

            if (parsed == null)
            {
                return;
            }

            lock (_lock)
            {
                _values = new Dictionary<string, JsonElement>(parsed, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
            // A broken file must not throw away state the plugin is still using.
        }
    }

    /// <summary>
    /// Writes the file, and never throws.
    ///
    /// A plugin calls this on every change, and a store that threw took the
    /// whole reload down with it when the file turned out to be unwritable. The
    /// values stay correct in memory whatever happens here, so failing quietly
    /// is right, but failing silently is not: an operator whose file cannot be
    /// written would otherwise watch a roster save and then vanish on restart.
    /// </summary>
    /// <returns>True when the file was written.</returns>
    public bool Save()
    {
        try
        {
            Dictionary<string, JsonElement> forDisk;

            lock (_lock)
            {
                forDisk = new Dictionary<string, JsonElement>(_values, StringComparer.OrdinalIgnoreCase);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllText(_path, JsonSerializer.Serialize(forDisk, Options));

            if (!Writable)
            {
                _log?.Invoke("INFO", $"'{_path}' can be written again");
            }

            Writable = true;
            _warned = false;

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
            or System.Security.SecurityException or NotSupportedException)
        {
            Writable = false;

            // Said once rather than on every change, since a plugin saves often.
            if (!_warned)
            {
                _warned = true;
                _log?.Invoke("WARN", $"Nothing can be saved to '{_path}': {e.Message}. " +
                                     "Changes are kept until the server stops, then lost.");
            }

            return false;
        }
    }
}
