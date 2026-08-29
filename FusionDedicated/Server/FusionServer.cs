using System.Runtime.InteropServices;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server.Safety;
using Steamworks;

namespace FusionDedicated.Server;

public sealed record ServerLogEntry(DateTime At, string Level, string Message);

/// <summary>
/// A headless Fusion host: accepts connections, runs the join handshake, allocates
/// IDs and relays traffic between clients.
///
/// It deliberately never takes ownership of an entity. In Fusion only an entity's
/// owner simulates it, so a server that owns nothing needs no physics at all — it
/// only has to remember what exists so late joiners can be caught up.
/// </summary>
public sealed class FusionServer : IDisposable
{
    public ServerConfig Config { get; }

    public PlayerRegistry Players { get; } = new();
    public EntityRegistry Entities { get; } = new();

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public SpawnGuard Guard { get; }
    public ResourceMonitor Resources { get; } = new();

    public long PacketsIn { get; private set; }
    public long PacketsOut { get; private set; }
    public long BytesIn { get; private set; }
    public long BytesOut { get; private set; }

    private HSteamListenSocket _listenSocket;
    private HSteamNetPollGroup _pollGroup;
    private Callback<SteamNetConnectionStatusChangedCallback_t>? _statusCallback;

    private readonly List<ServerLogEntry> _log = new();
    private readonly object _logLock = new();

    private BlocklistEvaluator _blocklist = new(new HashSet<string>(StringComparer.Ordinal));

    public FusionServer(ServerConfig config)
    {
        Config = config;
        Players.MaxPlayers = config.MaxPlayers;
        Guard = new SpawnGuard(config);
    }

    public void Start()
    {
        // A poll group lets every connection be drained with one call.
        _pollGroup = SteamNetworkingSockets.CreatePollGroup();

        _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);

        _statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>
            .Create(OnConnectionStatusChanged);

        RebuildBlocklist();

        Log("INFO", $"Relay socket listening as SteamID {SteamUser.GetSteamID().m_SteamID}");
    }

    public void RebuildBlocklist()
    {
        _blocklist = new BlocklistEvaluator(
            new HashSet<string>(Config.BlacklistedBarcodes, StringComparer.Ordinal));
    }

    public void Dispose()
    {
        _logFile?.Dispose();
        _statusCallback?.Dispose();

        if (_pollGroup.m_HSteamNetPollGroup != 0)
        {
            SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
        }

        if (_listenSocket.m_HSteamListenSocket != 0)
        {
            SteamNetworkingSockets.CloseListenSocket(_listenSocket);
        }
    }

    // ---- logging ----

    public IReadOnlyList<ServerLogEntry> RecentLog(int count = 200)
    {
        lock (_logLock)
        {
            return _log.TakeLast(count).ToList();
        }
    }

    private StreamWriter? _logFile;
    private DateTime _logFileDay = DateTime.MinValue;

    /// <summary>
    /// Appends to a dated file the server owns. stdout is redirected by the start
    /// command with '>', so that copy is wiped on every relaunch; this one is not.
    /// </summary>
    private void WriteToFile(ServerLogEntry entry)
    {
        try
        {
            var day = entry.At.Date;

            if (_logFile == null || day != _logFileDay)
            {
                _logFile?.Dispose();

                string dir = Path.IsPathRooted(Config.LogDirectory)
                    ? Config.LogDirectory
                    : Path.Combine(AppContext.BaseDirectory, Config.LogDirectory);

                Directory.CreateDirectory(dir);

                _logFile = new StreamWriter(
                    Path.Combine(dir, $"server-{day:yyyy-MM-dd}.log"), append: true)
                {
                    AutoFlush = true,
                };

                _logFileDay = day;
            }

            _logFile.WriteLine($"[{entry.At.ToLocalTime():HH:mm:ss}] {entry.Level,-5} {entry.Message}");
        }
        catch
        {
            // Logging must never be able to take the server down.
        }
    }

    public void Log(string level, string message)
    {
        var entry = new ServerLogEntry(DateTime.UtcNow, level, message);

        lock (_logLock)
        {
            _log.Add(entry);

            if (_log.Count > 2000)
            {
                _log.RemoveRange(0, 500);
            }

            WriteToFile(entry);
        }

        var colour = level switch
        {
            "ERROR" => ConsoleColor.Red,
            "WARN" => ConsoleColor.Yellow,
            "JOIN" => ConsoleColor.Green,
            "LEAVE" => ConsoleColor.Magenta,
            _ => ConsoleColor.Gray,
        };

        Console.ForegroundColor = colour;
        Console.WriteLine($"[{entry.At:HH:mm:ss}] {level,-5} {message}");
        Console.ResetColor();
    }

    // ---- connection lifecycle ----

    private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t info)
    {
        var connection = info.m_hConn;

        switch (info.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                if (Players.IsFull)
                {
                    Log("WARN", $"Refused a connection: server is full ({Players.Count}/{Players.MaxPlayers})");
                    SteamNetworkingSockets.CloseConnection(connection, 0, "Server full", false);
                    return;
                }

                SteamNetworkingSockets.AcceptConnection(connection);
                SteamNetworkingSockets.SetConnectionPollGroup(connection, _pollGroup);

                Log("INFO", $"Transport connected (conn {connection.m_HSteamNetConnection}), " +
                            "awaiting ConnectionRequest");
                return;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                HandleDisconnect(connection, info.m_info.m_szEndDebug);
                SteamNetworkingSockets.CloseConnection(connection, 0, null, false);
                return;
        }
    }

    private void HandleDisconnect(HSteamNetConnection connection, string reason)
    {
        var player = Players.Remove(connection);

        if (player == null)
        {
            return;
        }

        Log("LEAVE", $"{player.DisplayName} left (SmallID {player.SmallId}) — {reason}");

        NoteDeparture(player.DisplayName, reason);

        Guard.Forget(player.SmallId);

        // Their entities lost the only machine simulating them. Hand them to another
        // player if anyone is left, otherwise they hang frozen until culled.
        byte? heir = Players.Players.FirstOrDefault()?.SmallId;
        var affected = Entities.Orphan(player.SmallId, heir);

        if (affected.Count > 0)
        {
            Log("INFO", heir.HasValue
                ? $"{affected.Count} entities handed to player {heir}"
                : $"{affected.Count} entities left without an owner");
        }

        Broadcast(ServerProtocol.WriteDisconnect(player.PlatformId, "Player left"), reliable: true);

        PushSettings();
    }

    /// <summary>
    /// Whether a disconnect reason indicates something went wrong rather than
    /// somebody simply leaving. "Closing Connection" is the ordinary path; a timeout
    /// means their client stopped responding while the relay was still healthy.
    /// </summary>
    private static bool IsFault(string reason)
        => reason.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("problem", StringComparison.OrdinalIgnoreCase)
        || reason.Contains("Bad cert", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tells every client to remove these entities. Attributed to a connected player
    /// because clients ignore a despawn from a sender they cannot resolve, and the
    /// relay itself is never announced as one.
    /// </summary>
    private void DespawnOnClients(IEnumerable<ushort> ids)
    {
        byte despawner = Players.Players.FirstOrDefault()?.SmallId ?? PlayerRegistry.ServerSmallId;

        foreach (ushort id in ids)
        {
            Broadcast(ServerProtocol.WriteDespawnResponse(despawner, id, false), reliable: true);
        }
    }

    /// <summary>Recent departures, used to spot a whole lobby emptying at once.</summary>
    private readonly List<(DateTime At, string Who, string Reason)> _departures = new();

    /// <summary>
    /// Players leaving one by one is ordinary. Several going within a few seconds of
    /// each other is not — it means one shared cause, and the reasons the transport
    /// gave are the only clue to what it was. Worth calling out loudly at the time,
    /// because reconstructing it afterwards is close to impossible.
    /// </summary>
    private void NoteDeparture(string who, string reason)
    {
        var now = DateTime.UtcNow;
        var window = TimeSpan.FromSeconds(15);

        lock (_logLock)
        {
            _departures.RemoveAll(d => now - d.At > window);
            _departures.Add((now, who, reason));
        }

        List<(DateTime At, string Who, string Reason)> recent;

        lock (_logLock)
        {
            recent = _departures.ToList();
        }

        if (recent.Count < 3)
        {
            return;
        }

        var span = (now - recent[0].At).TotalSeconds;

        // People do leave together — friends finishing a session look exactly like a
        // fault if you only count departures. What separates the two is the reason
        // the transport gave: a clean close is somebody choosing to go, while a
        // timeout means their client stopped answering.
        int faults = recent.Count(d => IsFault(d.Reason));

        if (faults < 2)
        {
            Log("INFO", $"{recent.Count} players left within {span:F1}s, all disconnecting " +
                        $"normally — {Players.Count} remain");
            return;
        }

        Log("ERROR", $"MASS DISCONNECT: {faults} of {recent.Count} departures in {span:F1}s " +
                     $"look like faults — {Players.Count} remain, {Entities.Count} entities in world");

        foreach (var (at, name, why) in recent)
        {
            Log("ERROR", $"  {at.ToLocalTime():HH:mm:ss}  {(IsFault(why) ? "FAULT" : "clean")}  {name} — {why}");
        }
    }

    // ---- receive pump ----

    private readonly IntPtr[] _messageBuffer = new IntPtr[128];

    public void Receive()
    {
        int count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_pollGroup, _messageBuffer, _messageBuffer.Length);

        for (var i = 0; i < count; i++)
        {
            try
            {
                var native = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_messageBuffer[i]);

                var bytes = new byte[native.m_cbSize];
                Marshal.Copy(native.m_pData, bytes, 0, native.m_cbSize);

                PacketsIn++;
                BytesIn += bytes.Length;

                HandleMessage(native.m_conn, bytes);
            }
            catch (Exception ex)
            {
                Log("ERROR", $"Failed to handle a packet: {ex.Message}");
            }
            finally
            {
                SteamNetworkingMessage_t.Release(_messageBuffer[i]);
            }
        }
    }

    private void HandleMessage(HSteamNetConnection connection, byte[] message)
    {
        if (message.Length < 1)
        {
            return;
        }

        byte tag = message[0];
        var sender = Players.GetByConnection(connection);

        if (sender != null)
        {
            // Their socket is still closing; nothing they send now should be acted on.
            if (sender.Kicked)
            {
                return;
            }

            sender.LastSeen = DateTime.UtcNow;
            sender.BytesIn += message.Length;
        }
        else
        {
            // Anything arriving before a player is registered is part of the join
            // attempt — a path worth seeing in full while it is still new.
            Log("INFO", $"Packet from an unidentified connection: tag={tag}, {message.Length} bytes, " +
                        $"hex={Convert.ToHexString(message.AsSpan(0, Math.Min(message.Length, 24)))}");
        }

        switch (tag)
        {
            case FusionProtocol.TagConnectionRequest:
                HandleConnectionRequest(connection, message);
                return;

            case FusionProtocol.TagSpawnRequest when sender != null:
                HandleSpawnRequest(sender, message);
                return;

            case FusionProtocol.TagEntityOwnershipRequest when sender != null:
                HandleOwnershipRequest(sender, message);
                return;

            case ServerProtocol.TagModInfoRequest when sender != null:
                HandleModInfoRequest(sender, message);
                return;

            case ServerProtocol.TagModInfoResponse when sender != null:
                HandleModInfoResponse(sender, message);
                return;

            case ServerProtocol.TagDespawnRequest when sender != null:
                HandleDespawnRequest(sender, message);
                return;

            case ServerProtocol.TagPermissionCommandRequest when sender != null:
                HandlePermissionCommand(sender, message);
                return;

            case FusionProtocol.TagEntityPoseUpdate when sender != null:
                TrackEntityPose(message);
                break;
        }

        if (sender != null)
        {
            Relay(sender, message);
        }
    }

    // ---- join handshake ----

    private void HandleConnectionRequest(HSteamNetConnection connection, byte[] message)
    {
        var request = ServerProtocol.TryReadConnectionRequest(message);

        if (request == null)
        {
            Log("WARN", "ConnectionRequest did not parse — rejecting");
            SteamNetworkingSockets.CloseConnection(connection, 0, "Bad request", false);
            return;
        }

        // The identity on the connection is authoritative; the payload's id is a fallback.
        ulong platformId = request.PlatformId;

        if (SteamNetworkingSockets.GetConnectionInfo(connection, out var info)
            && info.m_identityRemote.GetSteamID64() != 0)
        {
            platformId = info.m_identityRemote.GetSteamID64();
        }

        void Reject(string reason)
        {
            Log("WARN", $"Rejected {platformId}: {reason}");
            SendTo(connection, ServerProtocol.WriteDisconnect(platformId, reason), reliable: true);
        }

        if (Players.Contains(platformId))
        {
            Reject("You attempted to join, but the server detects you as already in it?");
            return;
        }

        if (Players.IsFull)
        {
            Reject("Server is full! Wait for someone to leave.");
            return;
        }

        if (Config.FindBan(platformId) is { } ban)
        {
            Reject(ban.Reason);
            return;
        }

        if (request.Version.Major != Config.VersionMajor || request.Version.Minor != Config.VersionMinor)
        {
            Reject($"Version mismatch: server is v{Config.VersionMajor}.{Config.VersionMinor}");
            return;
        }

        byte? smallId = Players.AllocateSmallId();

        if (smallId == null)
        {
            Reject("Server ran out of space! Wait for someone to leave.");
            return;
        }

        var player = new ConnectedPlayer
        {
            Connection = connection,
            PlatformId = platformId,
            SmallId = smallId.Value,
            AvatarBarcode = request.AvatarBarcode,
            AvatarStats = request.AvatarStats,
            Metadata = request.Metadata,
            EquippedItems = request.EquippedItems,
            Version = request.Version,
        };

        player.Username = request.Metadata.GetValueOrDefault("Username", "");
        player.Nickname = request.Metadata.GetValueOrDefault("Nickname", "");

        // The client sends its own idea of its permission level; the server's list is
        // what counts, so overwrite it before anyone else sees the metadata.
        player.Permission = Config.GetPermission(platformId);
        player.Metadata[PermissionMetadataKey] = player.Permission.ToFusionString();

        Players.Add(player);

        Log("JOIN", $"{player.DisplayName} joined — SmallID {player.SmallId}, " +
                    $"v{request.Version.Major}.{request.Version.Minor}, " +
                    $"{player.Permission.ToFusionString()}, avatar '{request.AvatarBarcode}'");

        // 1. Announce the newcomer to everyone, including themselves.
        Broadcast(ServerProtocol.WriteConnectionResponse(player.PlatformId, player.SmallId,
            player.Metadata, player.EquippedItems, player.AvatarBarcode, player.AvatarStats, true), reliable: true);

        // 2. Catch the newcomer up on everyone already here.
        foreach (var existing in Players.Players.Where(p => p.SmallId != player.SmallId))
        {
            SendTo(connection, ServerProtocol.WriteConnectionResponse(existing.PlatformId, existing.SmallId,
                existing.Metadata, existing.EquippedItems, existing.AvatarBarcode, existing.AvatarStats, false),
                reliable: true);
        }

        // 3. Tell them which level to load.
        SendTo(connection, ServerProtocol.WriteSceneLoad(Config.LevelBarcode, Config.LoadingScreenBarcode),
            reliable: true);

        // 4. Gamemode metadata — empty on a plain relay.
        SendTo(connection, ServerProtocol.WriteEmptyDynamicsAssignment(), reliable: true);

        // 5. The rules: privacy, combat toggles and which level each action needs.
        SendTo(connection, ServerProtocol.WriteServerSettings(BuildLobbyInfoJson()), reliable: true);

        Log("INFO", $"Catch-up sent: {Players.Count - 1} players, level '{Config.LevelBarcode}'");

        // Everyone's copy of LobbyInfo now has a stale player list.
        PushSettings();
    }

    // ---- world bookkeeping ----

    private void HandleSpawnRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = TryReadSpawnRequest(message);

        if (request == null)
        {
            Log("WARN", $"SpawnRequest from {sender.DisplayName} did not parse");
            return;
        }

        var blockVerdict = _blocklist.Check(request.Value.Barcode);

        if (blockVerdict.Blocked)
        {
            Log("WARN", $"Spawn of '{request.Value.Barcode}' by {sender.DisplayName} " +
                        $"denied by the {blockVerdict.Layer} blocklist: {blockVerdict.Reason}");
            return;
        }

        if (Entities.Count >= Config.MaxEntities)
        {
            // Make room from abandoned props rather than refusing. A refused spawn is
            // invisible to the player — they pull the trigger and nothing happens —
            // and once the world is full it stays full, so every spawn after that
            // fails for everyone.
            var evicted = Entities.EvictOldest(Config.EvictBatchSize);

            if (evicted.Count > 0)
            {
                DespawnOnClients(evicted);
                Log("INFO", $"World at capacity — evicted {evicted.Count} abandoned entities");
            }

            if (Entities.Count >= Config.MaxEntities)
            {
                Log("WARN", $"Spawn denied: entity limit reached ({Config.MaxEntities}) " +
                            "and nothing was eligible for eviction");
                return;
            }
        }

        // Only what they actually spawned. The last player standing inherits everyone
        // else's leftovers — one player was holding 1071 entities after a night of
        // this — and counting those would have the guard purge and eventually kick
        // whoever stayed longest, for other people's props.
        int owned = Entities.Entities.Count(e => e.OwnerSmallId == sender.SmallId && !e.Inherited);
        var verdict = Guard.Check(sender, owned);

        if (Guard.ExemptOverrun is { } overrun)
        {
            Log("WARN", $"Spam guard: {overrun}");
        }

        if (!verdict.Allowed)
        {
            Log("WARN", $"Spam guard: {sender.DisplayName} {verdict.Reason}");

            if (verdict.Purge)
            {
                int purged = PurgeEntitiesOf(sender.SmallId);

                if (purged > 0)
                {
                    Log("WARN", $"Removed {purged} entities spawned by {sender.DisplayName}");
                }
            }

            if (verdict.Kick)
            {
                Kick(sender.SmallId, "Kicked for spawning too many items too quickly");
            }

            return;
        }

        RememberHolder(request.Value.Barcode, sender.SmallId);

        ushort entityId = Entities.AllocateId();

        Entities.Register(entityId, request.Value.Barcode, sender.SmallId,
            request.Value.X, request.Value.Y, request.Value.Z);

        Broadcast(FusionProtocol.BuildSpawnResponse(sender.SmallId, sender.SmallId, entityId,
            request.Value.Barcode, new Vec3(request.Value.X, request.Value.Y, request.Value.Z),
            request.Value.TrackerId), reliable: true);

        Log("INFO", $"Spawn: id={entityId} '{request.Value.Barcode}' by {sender.DisplayName}");
    }

    /// <summary>
    /// The spawn gun's delete mode sends a DespawnRequest, which is addressed to the
    /// server alone. Clients only remove an object when they receive a matching
    /// DespawnResponse, so the server has to answer or nothing ever disappears.
    /// </summary>
    private void HandleDespawnRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = ServerProtocol.TryReadDespawnRequest(message);

        if (request == null)
        {
            Log("WARN", $"DespawnRequest from {sender.DisplayName} did not parse");
            return;
        }

        var (entityId, despawnEffect) = request.Value;

        Entities.Remove(entityId);

        Broadcast(ServerProtocol.WriteDespawnResponse(sender.SmallId, entityId, despawnEffect),
            reliable: true);

        Log("INFO", $"Despawn: id={entityId} by {sender.DisplayName}");
    }

    // ---- moderation ----

    public const string PermissionMetadataKey = "PermissionLevel";

    /// <summary>
    /// Handles a moderation command a player issued from the in-game menu. The client
    /// hides the buttons it thinks you may not use, but that is only a hint — the
    /// server is the thing that actually decides.
    /// </summary>
    private void HandlePermissionCommand(ConnectedPlayer sender, byte[] message)
    {
        var request = ServerProtocol.TryReadPermissionCommand(message);

        if (request == null)
        {
            return;
        }

        var (command, targetId) = request.Value;

        if (!targetId.HasValue)
        {
            return;
        }

        var target = Players.Get(targetId.Value);

        if (target == null || target.SmallId == sender.SmallId)
        {
            return;
        }

        void Deny(string action, PermissionLevel required)
        {
            Log("WARN", $"{sender.DisplayName} tried to {action} {target.DisplayName} " +
                        $"but is {sender.Permission.ToFusionString()}, not {required.ToFusionString()}");
        }

        switch (command)
        {
            case ServerProtocol.PermissionCommand.Kick:
                if (!sender.Permission.IsAtLeast(Config.Kicking))
                {
                    Deny("kick", Config.Kicking);
                    return;
                }

                // Moderators cannot act on someone ranked at or above them.
                if (target.Permission >= sender.Permission)
                {
                    Deny("kick", sender.Permission);
                    return;
                }

                Log("WARN", $"{sender.DisplayName} kicked {target.DisplayName}");
                Kick(target.SmallId, $"Kicked by {target.DisplayName}");
                return;

            case ServerProtocol.PermissionCommand.Ban:
                if (!sender.Permission.IsAtLeast(Config.Banning))
                {
                    Deny("ban", Config.Banning);
                    return;
                }

                if (target.Permission >= sender.Permission)
                {
                    Deny("ban", sender.Permission);
                    return;
                }

                Log("WARN", $"{sender.DisplayName} banned {target.DisplayName}");
                Ban(target.PlatformId, target.Username, $"Banned by {sender.Username}");
                return;

            case ServerProtocol.PermissionCommand.TeleportToThem:
            case ServerProtocol.PermissionCommand.TeleportToMe:
                if (!sender.Permission.IsAtLeast(Config.Teleportation))
                {
                    Deny("teleport", Config.Teleportation);
                    return;
                }

                // Teleporting needs a rig position, which only a simulating client
                // knows. Pass it along and let the players work it out between them.
                Relay(sender, message);
                return;
        }
    }

    /// <summary>
    /// Answers a client that is missing the current level and wants to know which
    /// mod.io mod to fetch. A real host reads this out of its own installed pallets;
    /// a headless server has none, so it repeats what the operator configured.
    /// </summary>
    /// <summary>Who has been seen using each barcode, so requests can be brokered.</summary>
    private readonly Dictionary<string, HashSet<byte>> _barcodeHolders = new();

    /// <summary>Forwarded requests, so the reply can be matched back to its barcode.</summary>
    private readonly Dictionary<(byte Requester, uint Tracker), string> _pendingModInfo = new();

    private void RememberHolder(string barcode, byte smallId)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        if (!_barcodeHolders.TryGetValue(barcode, out var holders))
        {
            holders = new HashSet<byte>();
            _barcodeHolders[barcode] = holders;
        }

        holders.Add(smallId);
    }

    private void HandleModInfoRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = ServerProtocol.TryReadModInfoRequest(message);

        if (request == null)
        {
            return;
        }

        var (target, barcode, trackerId) = request.Value;

        // Only requests aimed at the host are ours to answer. One client asking
        // another must still be passed along untouched.
        if (target.HasValue && target.Value != PlayerRegistry.ServerSmallId)
        {
            Relay(sender, message);
            return;
        }

        int modId = -1;
        int? fileId = null;

        if (Config.FindMod(barcode) is { } learned)
        {
            modId = learned.ModId;
            fileId = learned.ModFileId;
        }
        else if (Config.Levels.FirstOrDefault(l => l.Barcode == barcode && l.ModId > 0) is { } level)
        {
            modId = level.ModId;
            fileId = level.ModFileId;
        }
        else if (barcode == Config.LevelBarcode && Config.LevelModId > 0)
        {
            modId = Config.LevelModId;
            fileId = Config.LevelModFileId;
        }

        if (modId > 0)
        {
            SendTo(sender.Connection,
                ServerProtocol.WriteModInfoResponse(sender.SmallId, modId, fileId, Config.ModPlatform, trackerId),
                reliable: true);

            Log("INFO", $"Answered {sender.DisplayName}: '{barcode}' is mod.io {modId}");
            return;
        }

        // The server owns no mods, so it cannot look this up itself — but whoever
        // spawned the item can. Hand the question to them; their reply is already
        // addressed back to the asker, and the server learns the answer in passing.
        byte? holder = FindHolder(barcode, except: sender.SmallId);

        if (holder.HasValue &&
            ServerProtocol.RetargetToTarget(ServerProtocol.StampSender(message, sender.SmallId), holder.Value)
                is { } forwarded &&
            Players.Get(holder.Value) is { } holderPlayer)
        {
            _pendingModInfo[(sender.SmallId, trackerId)] = barcode;

            SendTo(holderPlayer.Connection, forwarded, reliable: true);

            Log("INFO", $"Asked {holderPlayer.DisplayName} where '{barcode}' comes from, " +
                        $"for {sender.DisplayName}");
            return;
        }

        Log("WARN", $"{sender.DisplayName} asked for mod info on '{barcode}' — " +
                    "nobody here knows it, so they cannot download it");
    }

    /// <summary>Picks a connected player known to have a barcode.</summary>
    private byte? FindHolder(string barcode, byte except)
    {
        if (_barcodeHolders.TryGetValue(barcode, out var holders))
        {
            foreach (byte candidate in holders)
            {
                if (candidate != except && Players.Get(candidate) != null)
                {
                    return candidate;
                }
            }
        }

        // Fall back to whoever currently owns one in the world.
        var owner = Entities.Entities
            .FirstOrDefault(e => e.Barcode == barcode && e.OwnerSmallId.HasValue && e.OwnerSmallId != except);

        return owner?.OwnerSmallId is { } id && Players.Get(id) != null ? id : null;
    }

    /// <summary>
    /// Watches replies going past so the server builds up a catalogue of the mods its
    /// players use. Once learned, a barcode can be served directly — including to
    /// someone who joins long after the original owner left.
    /// </summary>
    private void HandleModInfoResponse(ConnectedPlayer sender, byte[] message)
    {
        var response = ServerProtocol.TryReadModInfoResponse(message);

        if (response is { } r && r.Target.HasValue &&
            _pendingModInfo.Remove((r.Target.Value, r.TrackerId), out string? barcode) &&
            r.ModId > 0)
        {
            RememberHolder(barcode, sender.SmallId);

            if (Config.LearnMod(barcode, r.ModId, r.ModFileId))
            {
                Config.Save(Program.ConfigPath);
                Log("INFO", $"Learned '{barcode}' is mod.io {r.ModId} — the server can serve it from now on");
            }
        }

        Relay(sender, message);
    }

    // ---- world control ----

    /// <summary>
    /// Removes every entity a player owns, telling all clients to despawn them.
    /// Used when the spam guard trips, so the flood is cleaned up rather than left
    /// hanging in everyone's world.
    /// </summary>
    public int PurgeEntitiesOf(byte smallId)
    {
        // If the owner has already gone, name someone who is still here — see
        // ClearAllEntities for why an unresolvable sender is ignored by clients.
        byte despawner = Players.Get(smallId) != null
            ? smallId
            : Players.Players.FirstOrDefault()?.SmallId ?? smallId;

        // Their own spawns only — sweeping up inherited props would delete the work of
        // players who have since left.
        var doomed = Entities.Entities
            .Where(e => e.OwnerSmallId == smallId && !e.Inherited)
            .Select(e => e.Id)
            .ToList();

        foreach (ushort id in doomed)
        {
            Entities.Remove(id);
            Broadcast(ServerProtocol.WriteDespawnResponse(despawner, id, false), reliable: true);
        }

        return doomed.Count;
    }

    /// <summary>
    /// Wipes the world.
    ///
    /// The despawn is attributed to a connected player rather than to the relay's own
    /// ID. A dedicated server never announces itself as a player, so small ID 0 names
    /// nobody the clients know about, and a despawn from a sender they cannot resolve
    /// is not acted on — the entity would disappear from the server's books while
    /// staying in everyone's world.
    /// </summary>
    public int ClearAllEntities()
    {
        byte despawner = Players.Players.FirstOrDefault()?.SmallId ?? PlayerRegistry.ServerSmallId;

        var doomed = Entities.Entities.Select(e => e.Id).ToList();

        foreach (ushort id in doomed)
        {
            Entities.Remove(id);
            Broadcast(ServerProtocol.WriteDespawnResponse(despawner, id, false), reliable: true);
        }

        if (doomed.Count > 0)
        {
            Log("WARN", $"Cleared {doomed.Count} entities from the world");
        }

        return doomed.Count;
    }

    /// <summary>
    /// Sends everyone to a different level. Entities belong to the level that was
    /// loaded when they spawned, so the world is dropped at the same time.
    /// </summary>
    public void SetLevel(string barcode, string title, int modId, int? modFileId)
    {
        Config.LevelBarcode = barcode;
        Config.LevelTitle = string.IsNullOrWhiteSpace(title) ? barcode : title;
        Config.LevelModId = modId;
        Config.LevelModFileId = modFileId;

        Entities.Clear();

        Broadcast(ServerProtocol.WriteSceneLoad(Config.LevelBarcode, Config.LoadingScreenBarcode),
            reliable: true);

        PushSettings();

        Log("INFO", $"Level changed to '{Config.LevelTitle}' ({barcode})" +
                    (modId > 0 ? $", mod.io {modId}" : ""));
    }

    /// <summary>
    /// Disconnects everyone with a warning, then re-launches the process. Kicking
    /// first means players get told why instead of watching a connection die.
    /// </summary>
    public async Task RestartAsync(string reason, int graceSeconds)
    {
        Log("WARN", $"Restart requested — disconnecting {Players.Count} players");

        foreach (var player in Players.Players)
        {
            Kick(player.SmallId, reason);
        }

        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(graceSeconds, 1, 30)));

        RestartRequested?.Invoke();
    }

    /// <summary>Raised once everyone has been told to leave and the process may go down.</summary>
    public event Action? RestartRequested;

    /// <summary>Changes a player's level and tells every client, without a reconnect.</summary>
    public void SetPermission(ulong platformId, string username, PermissionLevel level)
    {
        Config.SetPermission(platformId, username, level);

        var player = Players.GetByPlatformId(platformId);

        if (player != null)
        {
            player.Permission = level;
            player.Metadata[PermissionMetadataKey] = level.ToFusionString();

            Broadcast(ServerProtocol.WritePlayerMetadataResponse(
                player.SmallId, PermissionMetadataKey, level.ToFusionString()), reliable: true);

            Log("INFO", $"{player.DisplayName} is now {level.ToFusionString()}");
        }
        else
        {
            Log("INFO", $"{(string.IsNullOrWhiteSpace(username) ? platformId.ToString() : username)} " +
                        $"is now {level.ToFusionString()} (offline)");
        }
    }

    public void Ban(ulong platformId, string username, string reason)
    {
        Config.Ban(platformId, username, reason);

        var player = Players.GetByPlatformId(platformId);

        if (player != null)
        {
            Kick(player.SmallId, reason);
        }

        Log("WARN", $"Banned {(string.IsNullOrWhiteSpace(username) ? platformId.ToString() : username)}: {reason}");
    }

    public bool Unban(ulong platformId)
    {
        if (!Config.Unban(platformId))
        {
            return false;
        }

        Log("INFO", $"Unbanned {platformId}");
        return true;
    }

    // ---- settings ----

    /// <summary>Steam ID this server runs under; used as the LobbyInfo's lobby ID.</summary>
    public ulong HostPlatformId { get; set; }

    public string BuildLobbyInfoJson()
        => LobbyInfoBuilder.Serialize(Config, Players.Players, HostPlatformId);

    /// <summary>
    /// Pushes the current settings to everyone connected. Without this a change made
    /// in the panel would only reach players who join afterwards.
    /// </summary>
    public void PushSettings()
    {
        Players.MaxPlayers = Config.MaxPlayers;

        RebuildBlocklist();

        Broadcast(ServerProtocol.WriteServerSettings(BuildLobbyInfoJson()), reliable: true);
    }

    private void HandleOwnershipRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = TryReadOwnership(message);

        if (request == null)
        {
            return;
        }

        var (requestedOwner, entityId) = request.Value;

        Entities.SetOwner(entityId, requestedOwner);

        // The host's only job here is to confirm; it never claims anything itself.
        var payload = new FusionNetWriter(8);
        payload.Write(requestedOwner);
        payload.WriteUInt16(entityId);

        var response = new FusionNetWriter(32);
        response.Write(FusionProtocol.TagEntityOwnershipResponse);
        response.Write((byte)2); // ToClients
        response.Write((byte)0); // Reliable
        response.WriteNullable(sender.SmallId);
        response.WriteBlock(payload.ToArray());

        Broadcast(response.ToArray(), reliable: true);
    }

    private void TrackEntityPose(byte[] message)
    {
        var pose = FusionProtocol.TryReadEntityPose(message);

        if (pose != null)
        {
            Entities.UpdatePosition(pose.Value.EntityId,
                pose.Value.Position.X, pose.Value.Position.Y, pose.Value.Position.Z);
        }
    }

    // ---- relaying ----

    private void Relay(ConnectedPlayer sender, byte[] message)
    {
        var (relayType, channel, target) = ServerProtocol.ReadRoute(message);

        bool reliable = channel != 1;
        var stamped = ServerProtocol.StampSender(message, sender.SmallId);

        switch (relayType)
        {
            case 0: // None — meant for the server alone
            case 1: // ToServer
                return;

            case 2: // ToClients — everyone, sender included
                Broadcast(stamped, reliable);
                return;

            case 3: // ToOtherClients
                Broadcast(stamped, reliable, except: sender.SmallId);
                return;

            case 4: // ToTarget
                if (target.HasValue && Players.Get(target.Value) is { } recipient)
                {
                    SendTo(recipient.Connection, stamped, reliable);
                }

                return;

            default:
                Broadcast(stamped, reliable, except: sender.SmallId);
                return;
        }
    }

    public void Broadcast(byte[] message, bool reliable, byte? except = null)
    {
        foreach (var player in Players.Players)
        {
            if (except.HasValue && player.SmallId == except.Value)
            {
                continue;
            }

            SendTo(player.Connection, message, reliable);
            player.BytesOut += message.Length;
        }
    }

    public void SendTo(HSteamNetConnection connection, byte[] message, bool reliable)
    {
        var buffer = Marshal.AllocHGlobal(message.Length);

        try
        {
            Marshal.Copy(message, 0, buffer, message.Length);

            int flags = reliable
                ? Constants.k_nSteamNetworkingSend_Reliable
                : Constants.k_nSteamNetworkingSend_Unreliable;

            SteamNetworkingSockets.SendMessageToConnection(connection, buffer, (uint)message.Length,
                flags, out _);

            PacketsOut++;
            BytesOut += message.Length;
        }
        catch
        {
            // A closing connection throws; the status callback handles cleanup.
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Kick(byte smallId, string reason)
    {
        var player = Players.Get(smallId);

        if (player == null || player.Kicked)
        {
            return;
        }

        player.Kicked = true;

        Log("WARN", $"Kicked {player.DisplayName}: {reason}");

        SendTo(player.Connection, ServerProtocol.WriteDisconnect(player.PlatformId, reason), reliable: true);

        var connection = player.Connection;

        Task.Delay(250).ContinueWith(_ =>
        {
            SteamNetworkingSockets.CloseConnection(connection, 0, reason, false);
        });
    }

    /// <summary>Periodic housekeeping: republish settings and drop dead entities.</summary>
    public void Tick()
    {
        // A client builds its rules purely from the ServerSettings message. If it ever
        // misses one — or its own scene-load hook overwrites LobbyInfo with local
        // preferences — it falls back to LobbyInfo.Empty, where mortality and knockout
        // are both off. In game that looks like being unkillable with nothing happening
        // on death, so it is worth a small reliable message to keep everyone converged.
        if (Players.Count > 0)
        {
            PushSettings();
        }

        if (!Config.CullOrphanedEntities)
        {
            return;
        }

        var removed = Entities.CullStale(
            TimeSpan.FromSeconds(Config.OrphanTimeoutSeconds),
            TimeSpan.FromSeconds(Config.InheritedTimeoutSeconds));

        if (removed.Count > 0)
        {
            // Forgetting them here is not enough: they stay in every client's world,
            // and their ids go back in the pool. Since ids are only a ushort, a busy
            // server works its way round the range in about a week and would then
            // hand a recycled id to a prop the clients still have — two different
            // objects under one id. So the cull has to be broadcast, not just booked.
            DespawnOnClients(removed);

            Log("INFO", $"Culled {removed.Count} abandoned entities " +
                        $"({Entities.Count} left in world)");
        }

        // Warn before the allocator laps rather than after.
        if (Entities.NextId > ushort.MaxValue - 4096)
        {
            Log("WARN", $"Entity ids near the top of the range ({Entities.NextId}/{ushort.MaxValue}); " +
                        "the allocator will wrap and reuse freed ids");
        }
    }

    // ---- small readers ----

    private static (string Barcode, float X, float Y, float Z, uint TrackerId)? TryReadSpawnRequest(byte[] message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            reader.ReadByte(); // tag
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            if (relayType != 0)
            {
                reader.ReadNullableByte();
            }

            reader.ReadInt32(); // payload length

            string barcode = reader.ReadString() ?? "";

            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();

            reader.ReadRaw(7); // compressed rotation

            return (barcode, x, y, z, reader.ReadUInt32());
        }
        catch
        {
            return null;
        }
    }

    private static (byte Owner, ushort EntityId)? TryReadOwnership(byte[] message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            reader.ReadByte(); // tag
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            if (relayType != 0)
            {
                reader.ReadNullableByte();
            }

            reader.ReadInt32(); // payload length

            return (reader.ReadByte(), reader.ReadUInt16());
        }
        catch
        {
            return null;
        }
    }
}
