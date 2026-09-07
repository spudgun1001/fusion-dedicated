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

    /// <summary>
    /// For the other admins rather than the banned player, who is shown the reason.
    /// Written after the ban as often as with it.
    /// </summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    /// <summary>When the ban lifts. Null is permanent, which is the default.</summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    public bool HasExpired => ExpiresAt is { } at && at <= DateTime.UtcNow;
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

    /// <summary>The active ban, or null when there is none or it has lapsed.</summary>
    public BanRecord? Find(ulong platformId)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(platformId, out var entry))
            {
                return null;
            }

            return entry.HasExpired ? null : entry;
        }
    }

    public bool IsBanned(ulong platformId) => Find(platformId) != null;

    public void Ban(ulong platformId, string name, string reason, TimeSpan? duration = null,
        string note = "")
    {
        lock (_lock)
        {
            // Somebody banned again is usually somebody already written about, so
            // the note carries over unless this call brings a new one.
            if (string.IsNullOrEmpty(note) && _entries.TryGetValue(platformId, out var existing))
            {
                note = existing.Note;
            }

            _entries[platformId] = new BanRecord
            {
                Name = name,
                Reason = string.IsNullOrWhiteSpace(reason) ? "Banned from Server" : reason,
                BannedAt = DateTime.UtcNow,
                ExpiresAt = duration is { } d ? DateTime.UtcNow + d : null,
                Note = note,
            };
        }
    }

    /// <summary>Writes the admin note on a ban. False when nobody is banned by that id.</summary>
    public bool SetNote(ulong platformId, string note)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(platformId, out var record))
            {
                return false;
            }

            record.Note = note ?? "";
            return true;
        }
    }

    /// <summary>Removes lapsed bans. Returns how many went.</summary>
    public int SweepExpired()
    {
        lock (_lock)
        {
            var gone = _entries.Where(e => e.Value.HasExpired).Select(e => e.Key).ToList();

            foreach (ulong id in gone)
            {
                _entries.Remove(id);
            }

            return gone.Count;
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
            var parsed = JsonSerializer.Deserialize<Dictionary<string, BanRecord>>(
                File.ReadAllText(_path), Options);

            if (parsed is null)
            {
                return true;
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
        catch (Exception e) when (e is JsonException or IOException
            or UnauthorizedAccessException)
        {
            // Keep the list we had rather than unbanning everyone over a stray comma.
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
