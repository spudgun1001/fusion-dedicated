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
    public Dictionary<string, string> Metadata { get; set; } = new();
    public List<string> EquippedItems { get; set; } = new();

    /// <summary>
    /// Level this player joined with. Mirrored into their Fusion metadata so every
    /// client shows the right badge, and re-checked here before honouring a command.
    /// </summary>
    public PermissionLevel Permission { get; set; } = PermissionLevel.Default;

    /// <summary>
    /// Set once a disconnect has been sent. The socket does not close instantly, so
    /// without this their in-flight packets keep being processed — which made one
    /// kick fire seven times.
    /// </summary>
    public bool Kicked { get; set; }

    public System.Version Version { get; set; } = new(0, 0, 0);
    public DateTime JoinedAt { get; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

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
