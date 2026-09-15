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

    /// <summary>One save at a time, so two threads never write the same temporary file.</summary>
    private readonly object _saveLock = new();

    /// <summary>Keys already warned about, so a plugin reading one every second says it once.</summary>
    private readonly HashSet<string> _unreadableKeys = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, JsonElement> _values = new(StringComparer.OrdinalIgnoreCase);
    private bool _warned;

    public PluginStore(string path, Action<string, string>? log = null)
    {
        _path = path;
        _log = log;
    }

    /// <summary>The file this store reads and writes, so a second store can sit beside it.</summary>
    internal string FilePath => _path;

    /// <summary>False once a write has failed and not yet succeeded again.</summary>
    public bool Writable { get; private set; } = true;

    public T? Get<T>(string key)
    {
        bool warn = false;

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
                warn = _unreadableKeys.Add(key);
            }
        }

        // Said outside the lock, like the store's other log lines.
        if (warn)
        {
            _log?.Invoke("WARN", $"'{_path}' has a '{key}' that could not be read as " +
                                 $"{typeof(T).Name}, so the plugin was given nothing for it");
        }

        return default;
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
                KeepUnreadable();
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
            KeepUnreadable();
        }
    }

    /// <summary>Copies a file that will not parse aside, before the next save writes over it.</summary>
    private void KeepUnreadable()
    {
        string copy = _path + ".unreadable";

        try
        {
            File.Copy(_path, copy, overwrite: true);
            _log?.Invoke("WARN", $"'{_path}' could not be read, so nothing was loaded from it. A copy is kept at '{copy}'.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke("WARN", $"'{_path}' could not be read, so nothing was loaded from it, and no copy could be kept: {e.Message}");
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
    public void Save() => TrySave();

    /// <summary>
    /// Save, and say whether it worked.
    ///
    /// Save itself returns void and always will. Changing its return type is a
    /// binary break: a plugin compiled against the old one asks the runtime for
    /// a method that no longer exists, and the MissingMethodException that
    /// follows took a live server down mid-session. Anything new goes beside it
    /// rather than through it.
    /// </summary>
    /// <returns>True when the file was written.</returns>
    public bool TrySave()
    {
        lock (_saveLock)
        {
            return WriteFile();
        }
    }

    private bool WriteFile()
    {
        string tmp = _path + ".tmp";

        try
        {
            Dictionary<string, JsonElement> forDisk;

            lock (_lock)
            {
                forDisk = new Dictionary<string, JsonElement>(_values, StringComparer.OrdinalIgnoreCase);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");

            // A rename replaces a read-only file on Linux, so the file must prove it can be written first.
            if (File.Exists(_path))
            {
                using (new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                }
            }

            // Written beside the file and moved over it, so a kill mid-write leaves the old file whole.
            File.WriteAllText(tmp, JsonSerializer.Serialize(forDisk, Options));
            File.Move(tmp, _path, overwrite: true);

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
            try { File.Delete(tmp); } catch { }

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
