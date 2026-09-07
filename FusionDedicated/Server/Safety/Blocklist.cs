using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Safety;

/// <summary>
/// The server owner's spawn rules, in a file they can edit or delete outright.
/// Deleting it removes this layer entirely; the community list and the operator's
/// own config are unaffected.
/// </summary>
public sealed class BlocklistFile
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Exact barcodes to refuse.</summary>
    [JsonPropertyName("barcodes")]
    public List<string> Barcodes { get; set; } = new();

    /// <summary>
    /// Substrings matched against the barcode, so a repackaged nuke nobody has
    /// enumerated is still caught. Matched case-insensitively.
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    /// <summary>Barcodes allowed even when a barcode or keyword rule would block them.</summary>
    [JsonPropertyName("whitelist")]
    public List<string> Whitelist { get; set; } = new();

    /// <summary>
    /// Words or whole barcodes that anyone may spawn whatever the Spawning rank
    /// says, for the things the game spawns during normal play. It lives here
    /// rather than in server.json because this file is reread when it is saved,
    /// and a magazine nobody can spawn is not worth a restart to fix.
    /// </summary>
    [JsonPropertyName("spawnExempt")]
    public List<string> SpawnExempt { get; set; } = new();

    /// <summary>Barcodes only Operator and above may spawn.</summary>
    [JsonPropertyName("operatorOnly")]
    public List<string> OperatorOnly { get; set; } = new();

    /// <summary>Hard per-player ceiling, which catches automated loops the burst guard rides out.</summary>
    [JsonPropertyName("maxSpawnsPerSecond")]
    public int MaxSpawnsPerSecond { get; set; } = 5;

    [JsonPropertyName("maxNicknameChangesPerMinute")]
    public int MaxNicknameChangesPerMinute { get; set; } = 3;

    /// <summary>Nicknames nobody may take, to blunt impersonation of staff.</summary>
    [JsonPropertyName("reservedNicknames")]
    public List<string> ReservedNicknames { get; set; } = new();
}

/// <summary>
/// Loads blocklist.json and reloads it when edited. An absent file is a valid
/// state meaning the owner turned this layer off.
/// </summary>
public sealed class BlocklistStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly string _path;

    public BlocklistStore(string path)
    {
        _path = path;
    }

    public BlocklistFile? Current { get; private set; }

    public DateTime LastWriteSeen { get; private set; }

    public void Load() => TryLoad();

    /// <summary>Reads the file, and says whether it managed to.</summary>
    public bool TryLoad()
    {
        if (!File.Exists(_path))
        {
            Current = null;
            return true;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<BlocklistFile>(File.ReadAllText(_path), Options);

            if (parsed != null)
            {
                Current = parsed;
            }
        }
        catch (Exception e) when (e is JsonException or IOException
            or UnauthorizedAccessException)
        {
            // Keep the list we already had rather than dropping every rule.
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
}
