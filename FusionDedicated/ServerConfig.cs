using System.Text.Json;
using System.Text.Json.Serialization;

namespace FusionDedicated;

/// <summary>
/// Mirrors LabFusion's PermissionLevel. Values must match — clients compare against
/// the numbers we publish in the lobby info.
/// </summary>
public enum PermissionLevel : sbyte
{
    Guest = -1,
    Default = 0,
    Operator = 1,
    Owner = 2,
}

public static class PermissionLevels
{
    /// <summary>The wire spelling Fusion expects in player metadata.</summary>
    public static string ToFusionString(this PermissionLevel level) => level switch
    {
        PermissionLevel.Guest => "GUEST",
        PermissionLevel.Operator => "OPERATOR",
        PermissionLevel.Owner => "OWNER",
        _ => "DEFAULT",
    };

    public static bool IsAtLeast(this PermissionLevel level, PermissionLevel required)
        => level >= required;

    public static PermissionLevel Clamp(int raw)
        => (PermissionLevel)Math.Clamp(raw, -1, 2);
}

public sealed class PermissionEntry
{
    public ulong PlatformId { get; set; }
    public string Username { get; set; } = "";
    public PermissionLevel Level { get; set; } = PermissionLevel.Default;
    public string Note { get; set; } = "";
}

public sealed class LevelEntry
{
    public string Title { get; set; } = "";
    public string Barcode { get; set; } = "";

    /// <summary>-1 for vanilla levels that need no download.</summary>
    public int ModId { get; set; } = -1;

    public int? ModFileId { get; set; }
}

/// <summary>
/// A barcode the server has learned the mod.io origin of. The server owns no mods
/// itself, so this is built up from what players tell it — see FusionServer's mod
/// info brokering.
/// </summary>
public sealed class ModCatalogEntry
{
    public string Barcode { get; set; } = "";
    public int ModId { get; set; } = -1;
    public int? ModFileId { get; set; }
    public DateTime LearnedAt { get; set; } = DateTime.UtcNow;
}

public sealed class BanEntry
{
    public ulong PlatformId { get; set; }
    public string Username { get; set; } = "";
    public string Reason { get; set; } = "Banned from Server";
    public DateTime BannedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ServerConfig
{
    // ---- identity ----

    /// <summary>Shown in the in-game server browser.</summary>
    public string ServerName { get; set; } = "Dedicated Fusion Server";

    public string Description { get; set; } = "Headless relay — no host required";

    /// <summary>0 public, 1 private, 2 friends only, 3 locked.</summary>
    public int Privacy { get; set; } = 0;

    public int MaxPlayers { get; set; } = 10;

    public string ServerCode { get; set; } = "";

    /// <summary>
    /// Clients refuse to join across a major/minor mismatch, so this has to track
    /// whatever Fusion build the players are on.
    /// </summary>
    public int VersionMajor { get; set; } = 1;

    public int VersionMinor { get; set; } = 14;
    public int VersionPatch { get; set; } = 2;

    // ---- world ----

    /// <summary>The level joiners are told to load.</summary>
    public string LevelBarcode { get; set; } = "fa534c5a868247138f50c62e424c4144.Level.VoidG114";

    public string LevelTitle { get; set; } = "15 - Void G114";

    public string LoadingScreenBarcode { get; set; } = "";

    /// <summary>
    /// mod.io id of the level's mod, or -1 for a vanilla level. Two things read it:
    /// the server browser downloads that mod's thumbnail and shows it as the server's
    /// picture, and a client missing the level asks the host for this id so it can
    /// download the map itself.
    /// </summary>
    public int LevelModId { get; set; } = -1;

    /// <summary>Specific modfile id. Null lets the client take the newest build.</summary>
    public int? LevelModFileId { get; set; }

    /// <summary>
    /// Platform string the mod files were built for. A client whose platform differs
    /// ignores the file id and resolves the newest build for its own platform instead.
    /// </summary>
    public string ModPlatform { get; set; } = "windows";

    /// <summary>Levels offered in the panel's map picker.</summary>
    public List<LevelEntry> Levels { get; set; } = new();

    /// <summary>
    /// Barcodes whose mod.io origin the server has learned from players. Persisted, so
    /// once anyone has brought a mod into the server it can be handed to newcomers
    /// even after the original owner leaves.
    /// </summary>
    public List<ModCatalogEntry> ModCatalog { get; set; } = new();

    // ---- gameplay (pushed to clients as LobbyInfo) ----

    public bool NameTags { get; set; } = true;
    public bool VoiceChat { get; set; } = true;
    public bool PlayerConstraining { get; set; }
    public bool Mortality { get; set; } = true;
    public bool FriendlyFire { get; set; }
    public bool Knockout { get; set; }
    public int KnockoutLength { get; set; } = 10;
    public float MaxAvatarHeight { get; set; } = 20f;

    /// <summary>TimeScaleMode: 0 disabled, 1 low gravity, 2 host only, 3 everyone, 4 client side.</summary>
    public int SlowMoMode { get; set; } = 1;

    public int TimeBetweenGamemodeRounds { get; set; } = 30;

    // ---- permission gates ----
    // Each names the minimum level a player needs to use that feature. Clients gate
    // their own UI on these, and the server re-checks anything it can enforce.

    public PermissionLevel DevTools { get; set; } = PermissionLevel.Default;
    public PermissionLevel Constrainer { get; set; } = PermissionLevel.Default;
    public PermissionLevel CustomAvatars { get; set; } = PermissionLevel.Default;
    public PermissionLevel Kicking { get; set; } = PermissionLevel.Operator;
    public PermissionLevel Banning { get; set; } = PermissionLevel.Operator;
    public PermissionLevel Teleportation { get; set; } = PermissionLevel.Operator;

    /// <summary>
    /// Per-player levels, remembered across sessions by SteamID. A player not listed
    /// here joins as <see cref="PermissionLevel.Default"/>.
    /// </summary>
    public List<PermissionEntry> Permissions { get; set; } = new();

    public List<BanEntry> Bans { get; set; } = new();

    /// <summary>
    /// Older config files stored bans as a bare id list. Kept so an existing
    /// server.json still loads; migrated into <see cref="Bans"/> on read.
    /// </summary>
    public List<ulong> BannedPlatformIds { get; set; } = new();

    // ---- limits ----

    /// <summary>Cap on tracked entities, to stop a spawn flood exhausting memory.</summary>
    public int MaxEntities { get; set; } = 2000;

    /// <summary>
    /// A dedicated server simulates nothing, so entities whose owner left have no one
    /// applying gravity to them — they hang in place. Culling keeps the world tidy.
    /// </summary>
    public bool CullOrphanedEntities { get; set; } = true;

    public int OrphanTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// How long an inherited prop may sit untouched before it is removed.
    ///
    /// When a player leaves, their props are handed to whoever is still here so they
    /// keep being simulated instead of freezing mid-air. The side effect is that they
    /// stop being ownerless, so orphan culling never sees them again and the world
    /// only ever grows. Left alone it reaches the entity cap and every spawn after
    /// that is refused. Anything a player is actually using keeps sending pose
    /// updates, so only genuinely abandoned props age out.
    /// </summary>
    public int InheritedTimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// When the world is at <see cref="MaxEntities"/>, drop this many of the oldest
    /// abandoned props to make room rather than refusing the spawn. Refusing looks
    /// like a broken server to the player pressing the trigger.
    /// </summary>
    public int EvictBatchSize { get; set; } = 32;

    public List<string> BlacklistedBarcodes { get; set; } = new();

    // ---- crash protection ----
    // A dedicated server never simulates anything, so a spawn flood costs it almost
    // nothing — but every client has to instantiate each prop, and enough of them at
    // once is what drops a whole lobby at the same moment.

    public bool AntiSpamEnabled { get; set; } = true;

    /// <summary>Spawns one player may request inside <see cref="SpawnWindowSeconds"/>.</summary>
    public int SpawnBurstLimit { get; set; } = 25;

    public int SpawnWindowSeconds { get; set; } = 5;

    /// <summary>Entities a single player may own before they are considered a flooder.</summary>
    public int MaxEntitiesPerPlayer { get; set; } = 300;

    /// <summary>
    /// Trips before a kick. The first strikes only purge the offending props, so an
    /// enthusiastic builder is slowed down rather than ejected.
    /// </summary>
    public int SpamStrikesBeforeKick { get; set; } = 3;

    /// <summary>
    /// Ranks at or above this are exempt from the spawn limits. Kept at Owner: an
    /// operator is a moderator, not someone who should be able to flood the lobby,
    /// and exempting them also makes the guard look broken when the person testing
    /// it is the one running the server.
    /// </summary>
    public PermissionLevel AntiSpamExemptLevel { get; set; } = PermissionLevel.Owner;

    // ---- panel ----

    /// <summary>
    /// Directory for the server's own append-only log. Kept separate from whatever
    /// the start command redirects stdout to, because that file gets truncated every
    /// time the server is relaunched — which is exactly when you least want to lose
    /// the history.
    /// </summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>Port for the web control panel.</summary>
    public int DashboardPort { get; set; } = 8778;

    /// <summary>
    /// Which interface the panel listens on.
    ///
    /// Defaults to loopback because the panel has no login of any kind: anyone who
    /// can reach it can kick, ban, restart the server and wipe the world. Set this to
    /// "+" only on a network you trust, and reach it over an SSH tunnel otherwise.
    /// </summary>
    public string DashboardHost { get; set; } = "localhost";

    /// <summary>Username for the panel's HTTP Basic prompt.</summary>
    public string DashboardUser { get; set; } = "admin";

    /// <summary>
    /// Panel password. Empty disables the check, which is only safe on loopback —
    /// see the bind refusal in Dashboard.Start.
    /// </summary>
    public string DashboardPassword { get; set; } = "";

    [JsonIgnore]
    public string Version => $"{VersionMajor}.{VersionMinor}.{VersionPatch}";

    // ---- lookups ----

    public PermissionLevel GetPermission(ulong platformId)
        => Permissions.FirstOrDefault(p => p.PlatformId == platformId)?.Level
           ?? PermissionLevel.Default;

    public void SetPermission(ulong platformId, string username, PermissionLevel level)
    {
        var existing = Permissions.FirstOrDefault(p => p.PlatformId == platformId);

        // Default is the implicit state, so storing it would just grow the file.
        if (level == PermissionLevel.Default)
        {
            if (existing != null)
            {
                Permissions.Remove(existing);
            }

            return;
        }

        if (existing == null)
        {
            Permissions.Add(new PermissionEntry
            {
                PlatformId = platformId,
                Username = username,
                Level = level,
            });

            return;
        }

        existing.Level = level;

        if (!string.IsNullOrWhiteSpace(username))
        {
            existing.Username = username;
        }
    }

    public ModCatalogEntry? FindMod(string barcode)
        => ModCatalog.FirstOrDefault(m => m.Barcode == barcode && m.ModId > 0);

    /// <summary>Records a learned barcode. Returns true if this was new.</summary>
    public bool LearnMod(string barcode, int modId, int? modFileId)
    {
        if (string.IsNullOrWhiteSpace(barcode) || modId <= 0)
        {
            return false;
        }

        var existing = ModCatalog.FirstOrDefault(m => m.Barcode == barcode);

        if (existing != null)
        {
            bool changed = existing.ModId != modId || existing.ModFileId != modFileId;

            existing.ModId = modId;
            existing.ModFileId = modFileId;

            return changed;
        }

        ModCatalog.Add(new ModCatalogEntry
        {
            Barcode = barcode,
            ModId = modId,
            ModFileId = modFileId,
        });

        return true;
    }

    public BanEntry? FindBan(ulong platformId)
        => Bans.FirstOrDefault(b => b.PlatformId == platformId);

    public bool IsBanned(ulong platformId) => FindBan(platformId) != null;

    public void Ban(ulong platformId, string username, string reason)
    {
        var existing = FindBan(platformId);

        if (existing != null)
        {
            existing.Reason = reason;
            return;
        }

        Bans.Add(new BanEntry
        {
            PlatformId = platformId,
            Username = username,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Banned from Server" : reason,
        });
    }

    public bool Unban(ulong platformId)
    {
        var existing = FindBan(platformId);

        if (existing == null)
        {
            return false;
        }

        Bans.Remove(existing);
        BannedPlatformIds.Remove(platformId);

        return true;
    }

    // ---- persistence ----

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ServerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new ServerConfig();
            fresh.Save(path);
            return fresh;
        }

        ServerConfig config;

        try
        {
            config = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Options)
                     ?? new ServerConfig();
        }
        catch
        {
            return new ServerConfig();
        }

        foreach (ulong legacy in config.BannedPlatformIds)
        {
            if (!config.IsBanned(legacy))
            {
                config.Bans.Add(new BanEntry { PlatformId = legacy, Username = "(imported)" });
            }
        }

        config.BannedPlatformIds.Clear();

        return config;
    }

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Not fatal — the server runs fine from in-memory defaults.
        }
    }
}
