using System.Text.Json;

namespace FusionDedicated.Server.Safety;

/// <summary>
/// Who may not speak. Keyed by SteamID so a mute survives a reconnect, and held in
/// memory only — a mute is a this-session thing, unlike a ban.
/// </summary>
public sealed class MuteList
{
    private readonly HashSet<ulong> _muted = new();
    private readonly object _lock = new();

    public IReadOnlyCollection<ulong> Muted
    {
        get { lock (_lock) { return _muted.ToList(); } }
    }

    public bool IsMuted(ulong platformId)
    {
        lock (_lock)
        {
            return _muted.Contains(platformId);
        }
    }

    /// <summary>Returns whether this changed anything.</summary>
    public bool Mute(ulong platformId)
    {
        lock (_lock)
        {
            return _muted.Add(platformId);
        }
    }

    public bool Unmute(ulong platformId)
    {
        lock (_lock)
        {
            return _muted.Remove(platformId);
        }
    }
}

/// <summary>
/// An optional members-only door. Off by default; when on, an unlisted player is
/// refused at the handshake with a reason rather than dropped silently.
/// </summary>
public sealed class Whitelist
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private Dictionary<ulong, string> _entries = new();
    private readonly object _lock = new();

    public Whitelist(string path)
    {
        _path = path;
    }

    public bool Enabled { get; set; }

    public IReadOnlyDictionary<ulong, string> Entries
    {
        get { lock (_lock) { return new Dictionary<ulong, string>(_entries); } }
    }

    public bool MayJoin(ulong platformId)
    {
        if (!Enabled)
        {
            return true;
        }

        lock (_lock)
        {
            return _entries.ContainsKey(platformId);
        }
    }

    public void Add(ulong platformId, string name)
    {
        lock (_lock)
        {
            _entries[platformId] = name;
        }
    }

    public bool Remove(ulong platformId)
    {
        lock (_lock)
        {
            return _entries.Remove(platformId);
        }
    }

    public DateTime LastWriteSeen { get; private set; }

    /// <summary>Reads the file, keeping the current list if it will not parse.</summary>
    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(_path), Options);

            if (parsed is null)
            {
                return;
            }

            var rebuilt = new Dictionary<ulong, string>();

            foreach (var (key, value) in parsed)
            {
                if (ulong.TryParse(key, out ulong id))
                {
                    rebuilt[id] = value;
                }
            }

            lock (_lock)
            {
                _entries = rebuilt;
            }
        }
        catch (JsonException)
        {
            // Keep the list we had. Dropping it would lock everyone out at once.
        }
    }

    public void Save()
    {
        try
        {
            Dictionary<string, string> forDisk;

            lock (_lock)
            {
                forDisk = _entries.ToDictionary(p => p.Key.ToString(), p => p.Value);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllText(_path, JsonSerializer.Serialize(forDisk, Options));

            LastWriteSeen = File.GetLastWriteTimeUtc(_path);
        }
        catch
        {
            // The list stays correct in memory until the next successful write.
        }
    }

    public bool ReloadIfChanged()
    {
        DateTime stamp;

        try
        {
            stamp = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        }
        catch
        {
            return false;
        }

        if (stamp == LastWriteSeen)
        {
            return false;
        }

        LastWriteSeen = stamp;
        Load();

        return true;
    }
}
