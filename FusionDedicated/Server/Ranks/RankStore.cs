using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Ranks;

public sealed class RankEntry
{
    [JsonPropertyName("rank")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PermissionLevel Rank { get; set; } = PermissionLevel.Default;

    /// <summary>For the operator's reference. Never trusted for identity.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// The rank roster, kept in its own file so it can be edited over SFTP and so a
/// stray comma cannot take the config with it.
/// </summary>
public sealed class RankStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private Dictionary<ulong, RankEntry> _entries = new();
    private readonly object _lock = new();

    public RankStore(string path)
    {
        _path = path;
    }

    /// <summary>A snapshot. The panel enumerates this while other threads write.</summary>
    public IReadOnlyDictionary<ulong, RankEntry> Entries
    {
        get { lock (_lock) { return new Dictionary<ulong, RankEntry>(_entries); } }
    }

    public PermissionLevel Get(ulong platformId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(platformId, out var entry) ? entry.Rank : PermissionLevel.Default;
        }
    }

    public void Set(ulong platformId, string username, PermissionLevel level)
    {
        lock (_lock)
        {
            if (level == PermissionLevel.Default)
            {
                _entries.Remove(platformId);
                return;
            }

            if (!_entries.TryGetValue(platformId, out var entry))
            {
                entry = new RankEntry();
                _entries[platformId] = entry;
            }

            entry.Rank = level;

            if (!string.IsNullOrWhiteSpace(username))
            {
                entry.Name = username;
            }
        }
    }

    /// <summary>Reads the file, keeping the current roster if it will not parse.</summary>
    public void Load() => TryLoad();

    /// <summary>Reads the file, and says whether it managed to.</summary>
    public bool TryLoad()
    {
        if (!File.Exists(_path))
        {
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, RankEntry>>(
                File.ReadAllText(_path), Options);

            if (parsed is null)
            {
                return true;
            }

            var rebuilt = new Dictionary<ulong, RankEntry>();

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
        catch (Exception e) when (e is JsonException or IOException
            or UnauthorizedAccessException)
        {
            // Keep whatever we already had rather than dropping every rank.
            //
            // A file being written at this moment throws IOException from the
            // read, not JsonException, and this is called from the main loop
            // every ten seconds. It escaped, nothing above caught it, and the
            // server died: the panel saving while the reload ran did it.
            //
            // Reported as a failure so the caller does not mark the file as read
            // and then never look at it again.
            return false;
        }

        return true;
    }

    public void Save()
    {
        try
        {
            Dictionary<string, RankEntry> forDisk;

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
            // Not fatal; ranks stay correct in memory until the next successful write.
        }
    }

    public int MigrateFrom(IEnumerable<PermissionEntry> existing)
    {
        var added = 0;

        foreach (var entry in existing)
        {
            lock (_lock)
            {
                if (_entries.ContainsKey(entry.PlatformId))
                {
                    continue;
                }
            }

            Set(entry.PlatformId, entry.Username, entry.Level);
            added++;
        }

        return added;
    }

    /// <summary>Merges an environment list. Never lowers a rank already held.</summary>
    public int MergeSeed(IEnumerable<ulong> ids, PermissionLevel level)
    {
        var added = 0;

        foreach (ulong id in ids)
        {
            string name;

            lock (_lock)
            {
                if (Get(id) >= level)
                {
                    continue;
                }

                name = _entries.TryGetValue(id, out var e) ? e.Name : "";
            }

            Set(id, name, level);
            added++;
        }

        return added;
    }

    public DateTime LastWriteSeen { get; private set; }

    /// <summary>Rereads the file when its timestamp has moved.</summary>
    public bool ReloadIfChanged()
    {
        DateTime stamp;

        try
        {
            if (!File.Exists(_path))
            {
                return false;
            }

            stamp = File.GetLastWriteTimeUtc(_path);
        }
        catch
        {
            return false;
        }

        if (stamp == LastWriteSeen)
        {
            return false;
        }

        // The stamp only moves once the file has actually been read. Advancing it
        // first meant a read that failed because the file was busy was never
        // retried: the edit was swallowed and stayed swallowed until somebody
        // touched the file again.
        if (!TryLoad())
        {
            return false;
        }

        LastWriteSeen = stamp;
        return true;
    }
}
