using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated.Server.Props;

/// <summary>A prop somebody placed on purpose and wants back after a restart.</summary>
public sealed class PersistentProp
{
    [JsonPropertyName("barcode")]
    public string Barcode { get; set; } = "";

    /// <summary>
    /// The level it belongs to. A prop placed in one map would otherwise turn up
    /// floating in the middle of another.
    /// </summary>
    [JsonPropertyName("level")]
    public string Level { get; set; } = "";

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    /// <summary>
    /// The seven rotation bytes, as hex, so the file stays readable and a prop
    /// comes back the way round it was placed.
    /// </summary>
    [JsonPropertyName("rotation")]
    public string Rotation { get; set; } = "";

    /// <summary>For the operator's reference when reading the file.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    public byte[] RotationBytes()
    {
        try
        {
            return string.IsNullOrWhiteSpace(Rotation)
                ? Array.Empty<byte>()
                : Convert.FromHexString(Rotation);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }
}

/// <summary>
/// Props that outlive a restart, kept in their own file so they can be edited by
/// hand and backed up on their own, the way ranks.json and bans.json are.
/// </summary>
public sealed class PersistentPropStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly object _lock = new();

    private List<PersistentProp> _props = new();

    public PersistentPropStore(string path) => _path = path;

    public IReadOnlyList<PersistentProp> All
    {
        get { lock (_lock) { return _props.ToList(); } }
    }

    /// <summary>What should be put back on this level, and nothing from any other.</summary>
    public IReadOnlyList<PersistentProp> For(string level)
    {
        lock (_lock)
        {
            return _props
                .Where(p => string.Equals(p.Level, level, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public void Add(PersistentProp prop)
    {
        lock (_lock)
        {
            _props.Add(prop);
        }
    }

    /// <summary>
    /// Removes the first prop matching a barcode and place on this level. Position
    /// is compared loosely, because a prop settles a little after it is dropped.
    /// </summary>
    public bool Remove(string barcode, string level, float x, float y, float z)
    {
        lock (_lock)
        {
            var found = _props.FirstOrDefault(p =>
                string.Equals(p.Barcode, barcode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Level, level, StringComparison.OrdinalIgnoreCase)
                && Near(p.X, x) && Near(p.Y, y) && Near(p.Z, z));

            return found != null && _props.Remove(found);
        }
    }

    public int Count
    {
        get { lock (_lock) { return _props.Count; } }
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.5f;

    /// <summary>Reads the file, keeping what is loaded if it will not parse.</summary>
    public void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<PersistentProp>>(
                File.ReadAllText(_path), Options);

            if (parsed == null)
            {
                return;
            }

            lock (_lock)
            {
                _props = parsed;
            }
        }
        catch (Exception e) when (e is JsonException or IOException
            or UnauthorizedAccessException)
        {
            // A broken file must not throw away props somebody placed, and a file
            // being written at this moment throws from the read rather than the
            // parse.
        }
    }

    public void Save()
    {
        try
        {
            List<PersistentProp> forDisk;

            lock (_lock)
            {
                forDisk = _props.ToList();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            File.WriteAllText(_path, JsonSerializer.Serialize(forDisk, Options));
        }
        catch (IOException)
        {
            // The list stays correct in memory until a write succeeds.
        }
    }
}
