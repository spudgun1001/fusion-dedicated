using BonelabServerBrowser.Fusion;
using Steamworks;

namespace FusionDedicated.Server;

public sealed class ConnectedPlayer
{
    public required HSteamNetConnection Connection { get; init; }
    public required ulong PlatformId { get; init; }
    public required byte SmallId { get; init; }

    public string Username { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string AvatarBarcode { get; set; } = "";
    public byte[] AvatarStats { get; set; } = Array.Empty<byte>();
    /// <summary>
    /// Their metadata. Written from the message loop when they change a key and
    /// from the panel when a rank changes, and read on the message loop when
    /// somebody joins, so every touch goes through the methods below rather than
    /// the dictionary itself.
    /// </summary>
    private readonly Dictionary<string, string> _metadata = new();
    private readonly List<string> _equipped = new();
    private readonly object _stateLock = new();

    /// <summary>The most keys and cosmetics to hold. Both come from the client.</summary>
    private const int MaxMetadataKeys = 64;
    private const int MaxEquippedItems = 128;
    private const int MaxMetadataLength = 256;

    /// <summary>A copy, safe to read while another thread is writing.</summary>
    public Dictionary<string, string> Metadata
    {
        get { lock (_stateLock) { return new Dictionary<string, string>(_metadata); } }
        set { lock (_stateLock) { Replace(_metadata, value); } }
    }

    public List<string> EquippedItems
    {
        get { lock (_stateLock) { return _equipped.ToList(); } }
        set
        {
            lock (_stateLock)
            {
                _equipped.Clear();
                _equipped.AddRange(value.Take(MaxEquippedItems));
            }
        }
    }

    /// <summary>
    /// Sets one key, within limits. Both the key and the value are whatever the
    /// client sent, and the whole dictionary is repeated to everybody who joins
    /// afterwards, so an unbounded one is paid for on every future join.
    /// </summary>
    public void SetMetadata(string key, string value)
    {
        if (key.Length is 0 or > MaxMetadataLength || value.Length > MaxMetadataLength)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_metadata.Count >= MaxMetadataKeys && !_metadata.ContainsKey(key))
            {
                return;
            }

            _metadata[key] = value;
        }
    }

    /// <summary>Puts a cosmetic on or takes it off, within the same kind of limit.</summary>
    public void SetEquipped(string barcode, bool equipped)
    {
        if (barcode.Length is 0 or > MaxMetadataLength)
        {
            return;
        }

        lock (_stateLock)
        {
            if (!equipped)
            {
                _equipped.RemoveAll(b => string.Equals(b, barcode, StringComparison.Ordinal));
                return;
            }

            if (_equipped.Count < MaxEquippedItems
                && !_equipped.Contains(barcode, StringComparer.Ordinal))
            {
                _equipped.Add(barcode);
            }
        }
    }

    private static void Replace(Dictionary<string, string> into, Dictionary<string, string> from)
    {
        into.Clear();

        foreach (var (key, value) in from.Take(MaxMetadataKeys))
        {
            into[key] = value;
        }
    }

    /// <summary>
    /// Whether the level's own state has been sent to them.
    ///
    /// A client decides when it says it has finished loading, and it is the same
    /// message every time, so without this it could ask for the whole level's
    /// state as often as it liked and have the server build and send all of it
    /// each time, on the thread everybody else's traffic runs on.
    /// </summary>
    public bool LevelStateSent { get; set; }

    /// <summary>
    /// Level this player joined with. Mirrored into their Fusion metadata so every
    /// client shows the right badge, and re-checked here before honouring a command.
    /// </summary>
    public PermissionLevel Permission { get; set; } = PermissionLevel.Default;

    /// <summary>
    /// Set once a disconnect has been sent. The socket does not close instantly, so
    /// without this their in-flight packets keep being processed, which made one
    /// kick fire seven times.
    /// </summary>
    public bool Kicked { get; set; }

    public System.Version Version { get; set; } = new(0, 0, 0);
    public DateTime JoinedAt { get; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last pelvis position this player reported. A dedicated server has no rig of
    /// its own, so this is the only place it can learn where anybody is standing,
    /// which teleporting needs.
    /// </summary>
    public Vec3 LastPosition { get; set; } = Vec3.Zero;

    public bool HasPosition { get; set; }

    public long BytesIn { get; set; }
    public long BytesOut { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Nickname)
        ? (string.IsNullOrWhiteSpace(Username) ? $"Player {SmallId}" : Username)
        : $"{Username} ({Nickname})";
}

/// <summary>
/// Allocates the small IDs Fusion uses to address players and tracks who is on.
/// The host itself normally holds ID 0; a dedicated server has no avatar, so it
/// reserves 0 for itself as the relay identity and hands out 1 upward.
/// </summary>
public sealed class PlayerRegistry
{
    /// <summary>The relay's own ID. Never assigned to a real player.</summary>
    public const byte ServerSmallId = 0;

    private readonly Dictionary<byte, ConnectedPlayer> _bySmallId = new();
    private readonly Dictionary<ulong, byte> _byPlatformId = new();
    private readonly object _lock = new();

    public int MaxPlayers { get; set; } = 10;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _bySmallId.Count;
            }
        }
    }

    public bool IsFull => Count >= MaxPlayers;

    public IReadOnlyList<ConnectedPlayer> Players
    {
        get
        {
            lock (_lock)
            {
                return _bySmallId.Values.OrderBy(p => p.SmallId).ToList();
            }
        }
    }

    public bool Contains(ulong platformId)
    {
        lock (_lock)
        {
            return _byPlatformId.ContainsKey(platformId);
        }
    }

    public ConnectedPlayer? Get(byte smallId)
    {
        lock (_lock)
        {
            return _bySmallId.GetValueOrDefault(smallId);
        }
    }

    public ConnectedPlayer? GetByPlatformId(ulong platformId)
    {
        lock (_lock)
        {
            return _bySmallId.Values.FirstOrDefault(p => p.PlatformId == platformId);
        }
    }

    public ConnectedPlayer? GetByConnection(HSteamNetConnection connection)
    {
        lock (_lock)
        {
            return _bySmallId.Values.FirstOrDefault(
                p => p.Connection.m_HSteamNetConnection == connection.m_HSteamNetConnection);
        }
    }

    /// <summary>
    /// Takes the lowest free ID. Fusion addresses players with a single byte, so the
    /// ceiling is 255 regardless of how high MaxPlayers is set.
    /// </summary>
    public byte? AllocateSmallId()
    {
        lock (_lock)
        {
            for (byte candidate = 1; candidate < byte.MaxValue; candidate++)
            {
                if (!_bySmallId.ContainsKey(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    public void Add(ConnectedPlayer player)
    {
        lock (_lock)
        {
            _bySmallId[player.SmallId] = player;
            _byPlatformId[player.PlatformId] = player.SmallId;
        }
    }

    public ConnectedPlayer? Remove(HSteamNetConnection connection)
    {
        lock (_lock)
        {
            var player = _bySmallId.Values.FirstOrDefault(
                p => p.Connection.m_HSteamNetConnection == connection.m_HSteamNetConnection);

            if (player == null)
            {
                return null;
            }

            _bySmallId.Remove(player.SmallId);
            _byPlatformId.Remove(player.PlatformId);

            return player;
        }
    }

    public ConnectedPlayer? RemoveBySmallId(byte smallId)
    {
        lock (_lock)
        {
            if (!_bySmallId.TryGetValue(smallId, out var player))
            {
                return null;
            }

            _bySmallId.Remove(smallId);
            _byPlatformId.Remove(player.PlatformId);

            return player;
        }
    }
}
