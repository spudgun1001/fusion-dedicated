using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Bans;

public sealed class BanRecord
{
    /// <summary>For the operator's reference. Never trusted for identity.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "Banned from Server";

    [JsonPropertyName("bannedAt")]
    public DateTime BannedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The ban list in its own file, so it can be edited over SFTP and audited without
/// wading through configuration. Mirrors how ranks.json works.
/// </summary>
public sealed class BanStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private Dictionary<ulong, BanRecord> _entries = new();
    private readonly object _lock = new();

    public BanStore(string path)
    {
        _path = path;
    }

    /// <summary>A snapshot. The panel enumerates this while other threads write.</summary>
    public IReadOnlyDictionary<ulong, BanRecord> Entries
    {
        get { lock (_lock) { return new Dictionary<ulong, BanRecord>(_entries); } }
    }

    public DateTime LastWriteSeen { get; private set; }

    public BanRecord? Find(ulong platformId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(platformId, out var entry) ? entry : null;
        }
    }

    public bool IsBanned(ulong platformId)
    {
        lock (_lock)
        {
            return _entries.ContainsKey(platformId);
        }
    }

    public void Ban(ulong platformId, string name, string reason)
    {
        lock (_lock)
        {
            _entries[platformId] = new BanRecord
            {
                Name = name,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Banned from Server" : reason,
                BannedAt = DateTime.UtcNow,
            };
        }
    }

    public bool Unban(ulong platformId)
    {
        lock (_lock)
        {
            return _entries.Remove(platformId);
        }
    }

    /// <summary>Reads the file, keeping the current list if it will not parse.</summary>
    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, BanRecord>>(
                File.ReadAllText(_path), Options);

            if (parsed is null)
            {
                return;
            }

            var rebuilt = new Dictionary<ulong, BanRecord>();

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
            // Keep the list we had rather than unbanning everyone over a stray comma.
        }
    }

    public void Save()
    {
        try
        {
            Dictionary<string, BanRecord> forDisk;

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
            // Bans stay correct in memory until the next successful write.
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

    /// <summary>Brings bans across from an older server.json. Never overwrites.</summary>
    public int MigrateFrom(IEnumerable<BanEntry> existing)
    {
        var added = 0;

        lock (_lock)
        {
            foreach (var entry in existing)
            {
                if (_entries.ContainsKey(entry.PlatformId))
                {
                    continue;
                }

                _entries[entry.PlatformId] = new BanRecord
                {
                    Name = entry.Username,
                    Reason = entry.Reason,
                    BannedAt = entry.BannedAt,
                };

                added++;
            }
        }

        return added;
    }
}
