using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server.Audit;
using FusionDedicated.Server.Safety;
using Steamworks;

namespace FusionDedicated.Server;

public sealed record ServerLogEntry(DateTime At, string Level, string Message);

/// <summary>
/// A headless Fusion host: accepts connections, runs the join handshake, allocates
/// IDs and relays traffic between clients.
///
/// It deliberately never takes ownership of an entity. In Fusion only an entity's
/// owner simulates it, so a server that owns nothing needs no physics at all, it
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

    private readonly ISocketTransport _transport;

    private readonly List<ServerLogEntry> _log = new();
    private readonly object _logLock = new();

    private BlocklistEvaluator _blocklist = new(new HashSet<string>(StringComparer.Ordinal));
    private SpawnRateLimiter _rateLimiter = new(0);
    private RefusalGuard _refusals = new(0, TimeSpan.FromSeconds(5));
    private NicknameGuard _nicknames = new(0, Array.Empty<string>());

    public FusionServer(ServerConfig config, ISocketTransport? transport = null)
    {
        _transport = transport ?? new SteamSocketTransport(message => Log("ERROR", $"Failed to read a packet: {message}"));
        Config = config;
        Players.MaxPlayers = config.MaxPlayers;
        Entities.Capacity = config.MaxEntities;
        Entities.Clock = () => Clock();
        Guard = new SpawnGuard(config);
        _refusals = new RefusalGuard(config.RefusalKickPerSecond, TimeSpan.FromSeconds(5));

        // A prop's saved variables go with it, or a busy level fills the cache and
        // newer props stop being replayed to anybody who joins.
        Entities.Removed += id => _rpcVariables.ForgetEntity(id);

        // Nobody holds a prop that has gone.
        Entities.Removed += id => _grabs.ForgetEntity(id);

        Entities.Removed += id => _seats.ForgetEntity(id);
        Entities.Removed += ForgetAttachments;

        // A removed id can be handed out again, so its throttle history must not linger.
        Entities.Removed += id => _poseLog.Forget(id);
    }

    public void Start()
    {
        _transport.Start(OnConnecting, HandleDisconnect);

        RebuildBlocklist();

        Log("INFO", $"Relay socket listening as SteamID {_transport.LocalSteamId}");
    }

    public SafetyListStore? SafetyLists { get; set; }

    /// <summary>When set, takes precedence over the config permission list.</summary>
    public Ranks.RankStore? Ranks { get; set; }

    /// <summary>The owner's editable blocklist.json, when present.</summary>
    public BlocklistStore? Blocklist { get; set; }

    /// <summary>Props that outlive a restart, when the file is in use.</summary>
    public Props.PersistentPropStore? Props { get; set; }

    /// <summary>Plugin events, when a host is running. Null means no plugins.</summary>
    public Plugins.PluginEvents? Plugins { get; set; }

    /// <summary>Module tags plugins have claimed, when a host is running.</summary>
    public Plugins.PluginModules? PluginModules { get; set; }

    /// <summary>
    /// The RPC surface plugins read and write through. Set by the host, along with
    /// the sender that turns a plugin's call into a message.
    /// </summary>
    public FusionDedicated.Plugins.PluginRpc? PluginRpc { get; set; }

    /// <summary>Records module messages nothing handled, for writing a plugin against.</summary>
    public Plugins.ModuleInspector ModuleInspector { get; } = new();

    /// <summary>When set, bans.json is authoritative over the config ban list.</summary>
    public Bans.BanStore? BanList { get; set; }

    /// <summary>Voice mutes, for this session only.</summary>
    public MuteList Mutes { get; } = new();

    /// <summary>Members-only door. Off unless the operator turns it on.</summary>
    public Whitelist? Members { get; set; }

    /// <summary>Record of who was moderated and how.</summary>
    public AuditLog? AuditTrail { get; set; }

    public void RebuildBlocklist()
    {
        var file = Blocklist?.Current;

        _blocklist = new BlocklistEvaluator(
            new HashSet<string>(Config.BlacklistedBarcodes, StringComparer.Ordinal),
            Config.GlobalListsEnabled ? SafetyLists?.Mods : null,
            Config.ModCatalog
                .Where(m => m.ModId > 0)
                .GroupBy(m => m.Barcode)
                .ToDictionary(g => g.Key, g => g.First().ModId, StringComparer.Ordinal),
            file,
            Config.ExtendedProtection);

        var limits = ExtendedLimits.Resolve(Config.ExtendedProtection, file);

        // Adjusted rather than replaced. This runs on every join, leave and kick,
        // and a new object each time threw away what everybody had spent, so the
        // per-second spawn cap reset for the whole server whenever anybody came
        // or went.
        _rateLimiter.SetLimit(limits.MaxSpawnsPerSecond);
        _nicknames.SetLimits(limits.MaxNicknameChangesPerMinute, limits.ReservedNicknames);
    }

    public void Dispose()
    {
        _logFile?.Dispose();
        _transport.Dispose();
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

    /// <param name="console">
    /// False for lines that arrive too often to read, such as every hit landed.
    /// They still reach the panel and the log file.
    /// </param>
    public void Log(string level, string message, bool console = true)
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

        if (!console)
        {
            return;
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

    private void OnConnecting(HSteamNetConnection connection)
    {
        if (Players.IsFull)
        {
            Log("WARN", $"Refused a connection: server is full ({Players.Count}/{Players.MaxPlayers})");
            _transport.Close(connection, "Server full");
            return;
        }

        _transport.Accept(connection);

        Log("INFO", $"Transport connected (conn {connection.m_HSteamNetConnection}), awaiting ConnectionRequest");
    }

    private void HandleDisconnect(HSteamNetConnection connection, string reason)
    {
        if (Players.Remove(connection) is { } player)
        {
            Depart(player, reason);
        }
    }

    /// <summary>
    /// Everything that has to happen when somebody is no longer here.
    ///
    /// Separate from the disconnect callback because a kick never reaches that
    /// callback: Steam does not raise one for a connection the server closes
    /// itself, only for one the peer closes or that faults. So a kicked or banned
    /// player was removed from their own game and left standing in everybody
    /// else's, holding entities nobody could clean up.
    ///
    /// Safe to reach twice. The peer's own close arrives moments later, and by
    /// then they are already out of the register, so this does not run again.
    /// </summary>
    /// <param name="announce">
    /// What the other clients are told, which is never the reason.
    ///
    /// The reason a socket closed is a Steam string like "Closing Connection",
    /// and the reason for a kick is whatever an operator typed: ban reasons name
    /// alt accounts and other servers and are nobody else's business. Both are
    /// for the log. Everybody else gets a plain sentence.
    /// </param>
    private void Depart(ConnectedPlayer player, string reason, string? announce = null)
    {
        Log("LEAVE", $"{player.DisplayName} left (SmallID {player.SmallId}), {reason}");

        Plugins?.Left.Raise(new Plugins.LeaveEvent(player.PlatformId, player.DisplayName));

        NoteDeparture(player.DisplayName, reason);

        Guard.Forget(player.SmallId);
        _rateLimiter.Forget(player.SmallId);
        _refusals.Forget(player.SmallId);
        _nicknames.Forget(player.SmallId);
        _ownershipRefusalLog.Remove(player.SmallId);
        SeatForgetRider(player.SmallId);

        // Small ids are reused, so the next holder of this one is a different
        // person with a different set of mods and has never been asked anything.
        _askedFor.RemoveWhere(a => a.Holder == player.SmallId);

        // The next player given this small id starts with empty hands.
        _grabs.ForgetPlayer(player.SmallId);

        // Their body slots go with them, so nobody is told to holster anything on
        // a rig that no longer exists. A rig's slots are keyed by its small id,
        // and the next player given that id would inherit them.
        IReadOnlyList<ushort> unholstered;

        lock (_cacheLock)
        {
            _slotted.ForgetSlots(slot => Entities.Get(slot)?.OwnerSmallId == player.SmallId);
            unholstered = _slotted.ForgetRig(player.SmallId);
        }

        foreach (ushort weapon in unholstered)
        {
            Entities.SetAttached(weapon, false);
        }

        foreach (var holders in _barcodeHolders.Values)
        {
            holders.Remove(player.SmallId);
        }

        // Their entities lost the only machine simulating them. A vehicle goes to
        // somebody still sitting in it and a held thing to somebody still holding
        // it, otherwise to another player if anyone is left, otherwise they hang
        // frozen until culled.
        byte? fallback = Players.Players.FirstOrDefault()?.SmallId;
        var affected = Entities.OrphanWith(player.SmallId, entity =>
            WorldCatchup.HeirFor(RidersOf(entity.Id), HoldersOf(entity.Id), fallback, player.SmallId));

        // Before the heirs, because a client that locked a vehicle to the driver who
        // left ignores any new owner until it has cleaned that driver up.
        Broadcast(ServerProtocol.WriteDisconnect(player.PlatformId, announce ?? "Player left"),
            reliable: true);

        // Every client has already dropped the owner of these, so unless they are
        // told who has them now nobody simulates them and they freeze.
        var heirs = new List<(ushort EntityId, ConnectedPlayer Heir)>();

        foreach (var entity in affected)
        {
            if (entity.OwnerSmallId is { } newOwner)
            {
                AnnounceOwner(entity.Id, newOwner);

                if (Players.Get(newOwner) is { } heir)
                {
                    heirs.Add((entity.Id, heir));
                }
            }
        }

        ReannounceHeirs(heirs);

        foreach (var handed in affected.GroupBy(e => e.OwnerSmallId))
        {
            Log("INFO", handed.Key.HasValue
                ? $"{handed.Count()} entities handed to player {handed.Key}"
                : $"{handed.Count()} entities left without an owner");
        }

        PushSettings();
    }

    /// <summary>
    /// Names the heirs again a moment later, while each still owns what it was given
    /// and is still here. A vehicle locked to the driver who left only lets go once
    /// the driver's seat is cleaned up, so a client can drop the first announcement.
    /// </summary>
    private void ReannounceHeirs(IReadOnlyList<(ushort EntityId, ConnectedPlayer Heir)> heirs)
    {
        if (heirs.Count == 0)
        {
            return;
        }

        Defer(TimeSpan.FromSeconds(1), () =>
        {
            int sent = 0;

            foreach (var (entityId, heir) in heirs)
            {
                if (Entities.Get(entityId)?.OwnerSmallId != heir.SmallId || Players.Get(heir.SmallId) != heir)
                {
                    continue;
                }

                AnnounceOwner(entityId, heir.SmallId);
                sent++;
            }

            if (sent > 0)
            {
                Log("INFO", $"Announced the new owner of {sent} inherited entity(ies) again", console: false);
            }
        });
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
        var doomed = ids.ToList();

        if (doomed.Count == 0)
        {
            return;
        }

        var players = Players.Players.ToList();
        var present = players.Select(p => p.SmallId).ToList();

        // Each player is told by somebody else, because a despawn credited to the
        // player receiving it is not acted on by their own client.
        foreach (var recipient in players)
        {
            byte despawner = DespawnAttribution.For(recipient.SmallId, present);

            foreach (ushort id in doomed)
            {
                SendTo(recipient.Connection,
                    ServerProtocol.WriteDespawnResponse(despawner, id, false), reliable: true);
            }
        }
    }

    /// <summary>Recent departures, used to spot a whole lobby emptying at once.</summary>
    private readonly List<(DateTime At, string Who, string Reason)> _departures = new();

    /// <summary>
    /// Players leaving one by one is ordinary. Several going within a few seconds of
    /// each other is not, it means one shared cause, and the reasons the transport
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

        // People do leave together, friends finishing a session look exactly like a
        // fault if you only count departures. What separates the two is the reason
        // the transport gave: a clean close is somebody choosing to go, while a
        // timeout means their client stopped answering.
        int faults = recent.Count(d => IsFault(d.Reason));

        if (faults < 2)
        {
            Log("INFO", $"{recent.Count} players left within {span:F1}s, all disconnecting " +
                        $"normally, {Players.Count} remain");
            return;
        }

        Log("ERROR", $"MASS DISCONNECT: {faults} of {recent.Count} departures in {span:F1}s " +
                     $"look like faults, {Players.Count} remain, {Entities.Count} entities in world");

        foreach (var (at, name, why) in recent)
        {
            Log("ERROR", $"  {at.ToLocalTime():HH:mm:ss}  {(IsFault(why) ? "FAULT" : "clean")}  {name}, {why}");
        }
    }

    // ---- receive pump ----

    /// <summary>
    /// Drains the poll group. One pass returns at most a bufferful, which at a 16ms
    /// tick caps throughput around 8000 messages a second, reachable on a busy
    /// server, and the backlog only grows once it is. Keep pulling until a short
    /// batch comes back, bounded so a flood cannot starve the rest of the loop.
    /// </summary>
    public void Receive()
    {
        for (var pass = 0; pass < MaxReceivePasses; pass++)
        {
            if (_transport.Receive(ReceiveBatchSize, HandlePacket) < ReceiveBatchSize)
            {
                return;
            }
        }
    }

    /// <summary>Bounds one tick at 2048 messages, leaving room for ticks and lobby updates.</summary>
    private const int MaxReceivePasses = 16;

    private const int ReceiveBatchSize = 128;

    private void HandlePacket(HSteamNetConnection connection, byte[] bytes)
    {
        PacketsIn++;
        BytesIn += bytes.Length;

        try
        {
            HandleMessage(connection, bytes);
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to handle a packet: {ex.Message}");
        }
    }

    private void HandleMessage(HSteamNetConnection connection, byte[] message)
    {
        if (message.Length < 1)
        {
            return;
        }

        byte tag = message[0];

        // Being torn down. Their entry has already gone, so nothing below would
        // recognise them anyway, and a ConnectionRequest would be taken as a new
        // arrival. It also keeps their in-flight packets out of the log.
        lock (_closingLock)
        {
            if (_closing.Contains(connection.m_HSteamNetConnection))
            {
                return;
            }
        }

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
            // attempt, a path worth seeing in full while it is still new.
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

            case FusionProtocol.TagPlayerRepSeat when sender != null:
                HandleSeat(sender, message);
                return;

            case FusionProtocol.TagEntityUnqueueRequest when sender != null:
                HandleUnqueueRequest(sender, message);
                return;

            case FusionProtocol.TagEntityDataRequest when sender != null:
                HandleEntityDataRequest(sender, message);
                return;

            case FusionProtocol.TagNetworkPropCreate when sender != null:
                NotePropCreate(sender, message);
                break;

            case 209 when sender != null:
            case 210 when sender != null:
            case 211 when sender != null:
            case 212 when sender != null:
            case 213 when sender != null:
            case 214 when sender != null:
            {
                // A variable is kept so a joiner can be told what it is now. An
                // event is a one-shot and there is nothing to keep.
                if (tag != 209)
                {
                    NoteRpcVariable(tag, sender.SmallId, message);
                }

                // A plugin gets it before anybody else, which is what lets a prop
                // built in Unity and shipped on mod.io be answered by the server.
                if (OfferRpcToPlugins(sender, tag, message) == FusionDedicated.Plugins.RpcActionKind.Drop)
                {
                    return;
                }

                break;
            }

            case GateProtocol.TagPointItemEquipState when sender != null:
                // Kept as it changes. The join catch-up sends each player's
                // cosmetics, and it was only ever what they arrived wearing.
                if (GateProtocol.TryReadEquipState(message) is var (barcode, equipped))
                {
                    sender.SetEquipped(barcode, equipped);
                }

                break;

            case GateProtocol.TagPlayerMetadataRequest when sender != null:
                HandleMetadataRequest(sender, message);
                return;

            case ModuleProtocol.TagModule when sender != null:
                HandleModuleMessage(sender, message);
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

            case FusionProtocol.TagPlayerPoseUpdate when sender != null:
                TrackPlayerPose(sender, message);
                break;

            case FusionProtocol.TagEntityPoseUpdate when sender != null:
                TrackEntityPose(sender, message);
                break;

            case FusionProtocol.TagPlayerRepGrab when sender != null:
                NoteGrab(sender, message);
                break;

            case FusionProtocol.TagPlayerRepRelease when sender != null:
                if (FusionProtocol.TryReadRelease(message) is { } releasedHand
                    && _grabs.Release(sender.SmallId, releasedHand) is { } released)
                {
                    PassToHolder(sender, released);
                }

                break;

            case FusionProtocol.TagEntityCullStatus when sender != null:
                // Watched, then passed on like anything else. Only the owner's
                // word counts, which is the same rule the clients apply.
                if (FusionProtocol.TryReadCullStatus(message) is var (culledId, culled)
                    && Entities.Get(culledId)?.OwnerSmallId == sender.SmallId)
                {
                    Entities.SetCulledForOwner(culledId, culled);
                }

                break;

            case FusionProtocol.TagPlayerVoiceChat when sender != null:
                if (Mutes.IsMuted(sender.PlatformId))
                {
                    return;
                }

                break;

            case GateProtocol.TagPlayerRepDamage when sender != null:
            {
                if (!PassesGates(sender, tag, message))
                {
                    return;
                }

                var (_, _, hitTarget) = ServerProtocol.ReadRoute(message);
                ulong targetPlatformId = hitTarget is { } targetSmall
                    ? Players.Get(targetSmall)?.PlatformId ?? 0UL
                    : 0UL;

                var pluginDamage = Plugins?.Damage.Raise(new Plugins.DamageEvent(
                    sender.PlatformId, sender.DisplayName, targetPlatformId,
                    GateProtocol.TryReadDamage(message) ?? 0f));

                if (pluginDamage is { Allowed: false })
                {
                    return;
                }

                RecordHit(sender, message);
                break;
            }

            case GateProtocol.TagPlayerRepAvatar when sender != null:
            {
                if (!PassesGates(sender, tag, message))
                {
                    return;
                }

                string worn = GateProtocol.TryReadAvatarBarcode(message) ?? "";

                var pluginAvatar = Plugins?.Avatar.Raise(new Plugins.AvatarEvent(
                    sender.PlatformId, sender.DisplayName, sender.Permission, worn));

                if (pluginAvatar is { Allowed: false })
                {
                    Log("WARN", $"{sender.DisplayName} tried to wear '{worn}', refused by a " +
                                $"plugin: {pluginAvatar.Reason}");
                    return;
                }

                // The measurements travel with the barcode and were never kept, so
                // a newcomer was told the proportions each player joined in.
                if (GateProtocol.TryReadAvatarStats(message) is { Length: > 0 } stats)
                {
                    sender.AvatarStats = stats;
                }

                // The panel and the lobby info read this, and it was only ever set
                // during the handshake, so everybody kept the avatar they arrived in.
                if (worn.Length > 0)
                {
                    sender.AvatarBarcode = worn;

                    RememberHolder(worn, sender.SmallId);
                    AskWhereItComesFrom(sender, worn);
                }

                // A new avatar is a new rig, with nothing in its hands.
                _grabs.ForgetPlayer(sender.SmallId);

                break;
            }

            case GateProtocol.TagPlayerRepTeleport when sender != null:
            {
                if (!PassesGates(sender, tag, message))
                {
                    return;
                }

                var pluginTeleport = Plugins?.Teleport.Raise(new Plugins.TeleportEvent(
                    sender.PlatformId, sender.DisplayName, sender.Permission));

                if (pluginTeleport is { Allowed: false })
                {
                    return;
                }

                break;
            }

            case GateProtocol.TagSlowMoButton when sender != null:
                if (!PassesGates(sender, tag, message))
                {
                    return;
                }

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
            Log("WARN", "ConnectionRequest did not parse, rejecting");
            _transport.Close(connection, "Bad request");
            return;
        }

        // The identity on the connection is authoritative; the payload's id is a fallback.
        ulong platformId = request.PlatformId;

        ulong remote = _transport.RemoteSteamId(connection);

        if (remote != 0)
        {
            platformId = remote;
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

        if (Members is { Enabled: true } members && !members.MayJoin(platformId))
        {
            Log("WARN", $"Refused {platformId}: not on the whitelist");
            _transport.Close(connection, "Not on this server's whitelist");
            return;
        }

        if (BanList?.Find(platformId) is { } fileBan)
        {
            Log("WARN", $"Refused {platformId}: banned ({fileBan.Reason})");
            _transport.Close(connection, fileBan.Reason);
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

        // Before a slot is given, so a plugin refusing costs nothing.
        var pluginJoin = Plugins?.Joining.Raise(new Plugins.JoinEvent(
            platformId, request.Metadata.GetValueOrDefault("Username", ""),
            Ranks?.Get(platformId) ?? Config.GetPermission(platformId)));

        if (pluginJoin is { Allowed: false })
        {
            Reject(pluginJoin.Reason);
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

        if (!string.IsNullOrWhiteSpace(player.Nickname))
        {
            var nickVerdict = _nicknames.Allow(player.SmallId, player.Nickname, DateTime.UtcNow);

            if (!nickVerdict.Allowed)
            {
                Log("WARN", $"{player.Username} joined with nickname '{player.Nickname}': " +
                            $"{nickVerdict.Reason}. Falling back to their Steam name.");
                player.Nickname = "";
            }
        }

        // The client sends its own idea of its permission level; the server's list is
        // what counts, so overwrite it before anyone else sees the metadata.
        player.Permission = Ranks?.Get(platformId) ?? Config.GetPermission(platformId);

        player.SetMetadata(PermissionMetadataKey, player.Permission.ToFusionString());

        if (GlobalBanCheck.Find(SafetyLists?.Bans, platformId) is { } globalBan)
        {
            Log("WARN", $"{player.DisplayName} is on Fusion's global ban list " +
                        $"as '{globalBan.Username}': {globalBan.Reason}. " +
                        "Not enforced, ban them here if you agree.");
        }

        Players.Add(player);

        Log("JOIN", $"{player.DisplayName} joined. SmallID {player.SmallId}, " +
                    $"v{request.Version.Major}.{request.Version.Minor}, " +
                    $"{player.Permission.ToFusionString()}, avatar '{request.AvatarBarcode}'");

        // 1. Announce the newcomer to everyone, including themselves.
        Broadcast(ServerProtocol.WriteConnectionResponse(player.PlatformId, player.SmallId,
            player.Metadata, player.EquippedItems, player.AvatarBarcode, player.AvatarStats, true), reliable: true);

        // 1b. The server itself as player 0, before anything that makes a client look
        //     for the host. A client that knew no player 0 threw building constraints.
        SendTo(connection, ServerPlayer.ConnectionResponse(HostPlatformId, Config.ServerName), reliable: true);

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

        // 4. Gamemode metadata, empty on a plain relay.
        SendTo(connection, ServerProtocol.WriteEmptyDynamicsAssignment(), reliable: true);

        // 5. The rules: privacy, combat toggles and which level each action needs.
        SendTo(connection, ServerProtocol.WriteServerSettings(BuildLobbyInfoJson()), reliable: true);

        // 6. What is already in the world. Nobody else does this: Fusion only
        //    sends creation catch-up when it is the host, and no client here is,
        //    so without this a player who joins sees an empty level and the guns
        //    everyone else is holding are not there.
        EnsurePersistentProps(player);

        int caught = SendWorldCatchup(player);
        int scene = SendSceneProps(player);
        int welds = SendConstraints(player);

        Log("INFO", $"Catch-up sent: {Players.Count - 1} players, {caught} entities, " +
                    $"{scene} scene objects, {welds} constraints, " +
                    $"level '{Config.LevelBarcode}'");

        // Everyone's copy of LobbyInfo now has a stale player list.
        PushSettings();

        // Holsters and loaded magazines, once their entities have had time to
        // exist on the newcomer's machine.
        ReseatAttachments(player);

        // Last, and after the newcomer has been registered and told it is in.
        //
        // This used to fire before Players.Add, which meant a plugin answering it
        // could neither see the person who had just joined nor send them
        // anything: a broadcast walks the register, and they were not in it yet.
        // LabRP's balance is sent that way, so a joining player was the one
        // person who never received it and their wrist HUD stayed empty until
        // somebody else joined behind them.
        Plugins?.Joined.Raise(new Plugins.JoinEvent(
            platformId, player.DisplayName, player.Permission));
    }

    // ---- world bookkeeping ----

    /// <summary>
    /// Gives a real id to something a client has networked on its own.
    ///
    /// A scene object is not networked until somebody interacts with it: grabs
    /// it, sits in it, or hits it hard enough. The client builds the entity
    /// locally, parks it under a temporary id, and asks the server for a real
    /// one. Only the host answers, so on a relay nothing did, and the client
    /// waited for ever.
    ///
    /// Everything downstream waits with it. The registration callback never
    /// fires, so NetworkPropCreate is never sent and nobody else ever hears the
    /// object exists. That is a destructible door that works alone and not
    /// together, and a passenger who never looks seated because the seat itself
    /// was still waiting for this.
    /// </summary>
    private void HandleUnqueueRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = FusionProtocol.TryReadUnqueueRequest(message);

        if (request == null)
        {
            Log("WARN", $"EntityUnqueueRequest from {sender.DisplayName} did not parse");
            return;
        }

        // The asker is named in the payload, but it is their own claim. Answering
        // whoever actually sent it stops one client asking for ids on another's
        // behalf and stuffing an id into somebody else's queue.
        ushort allocated = Entities.AllocateId();

        // An unqueue costs an entity that no cull reclaims, so it is held to the
        // same per player count as spawning. Without it, two thousand of these
        // filled the world permanently and nobody could spawn anything again.
        int owned = Entities.Entities.Count(
            e => e.OwnerSmallId == sender.SmallId && e.Discovered);

        bool room = Entities.Count < Config.MaxEntities * 2
            && (Config.MaxEntitiesPerPlayer <= 0 || owned < Config.MaxEntitiesPerPlayer);

        // Answered whatever happens. The client has no retry and no timeout: it
        // waits on this reply before the grab or the seat that asked for it can
        // proceed, so a silent refusal is that object dead for the session.
        // Above the cap the id is still given and simply not tracked.
        if (room)
        {
            // Registered as theirs, discovered rather than spawned: it was
            // already in the level, so a newcomer's own copy has it and the join
            // catch-up must not send it as a spawn.
            var entity = Entities.Register(allocated, "", sender.SmallId, 0, 0, 0);
            entity.Discovered = true;
            entity.PositionKnown = false;
        }
        else
        {
            if (_unqueueRefused.Add(sender.SmallId))
            {
                Log("WARN", $"Unqueue for {sender.DisplayName} answered but not tracked: " +
                            "at the entity limit");
            }
        }

        SendTo(sender.Connection, FusionProtocol.BuildUnqueueResponse(
            sender.SmallId, request.Value.QueuedId, allocated), reliable: true);
    }

    /// <summary>
    /// Passes on a metadata key a player set on themselves.
    ///
    /// Only the host answers this, so on a relay nothing did and a value never
    /// left the person who set it. Anything reading somebody else's metadata,
    /// which is how mods carry per-player state, saw nothing at all.
    ///
    /// The authority rule is Fusion's own: you may write your own keys, and an
    /// operator may write anybody's. A client naming somebody else is refused
    /// rather than trusted, since the name in the payload is only their claim.
    /// </summary>
    /// <summary>
    /// Remembers a scene object somebody networked, then lets it carry on to the
    /// other clients as usual.
    ///
    /// Kept so a player who joins later can be told the same id everybody else
    /// is using. Without it they never hear of the object, network it again under
    /// a second id, and every message either way about that object is discarded
    /// by the other side for the rest of the session: their seat is never seen,
    /// and a door they break stays whole for everyone else.
    /// </summary>
    private void NotePropCreate(ConnectedPlayer sender, byte[] message)
    {
        var prop = FusionProtocol.TryReadPropCreate(message);

        if (prop == null)
        {
            return;
        }

        // Keyed on what names the object inside the level, so the same object is
        // never remembered twice.
        var key = (prop.Value.Hash, prop.Value.Index);

        lock (_cacheLock)
        {
            // Room only for something already known once the ceiling is reached,
            // so a flood cannot push out what is real.
            if (_sceneProps.Count >= MaxCachedProps && !_sceneProps.ContainsKey(key))
            {
                if (_cacheFull.Add("props"))
                {
                    Log("WARN", $"Holding {MaxCachedProps} scene objects and not taking more. " +
                                "A level does not have this many, so somebody is sending them.");
                }

                return;
            }

            _sceneProps[key] = prop.Value with { OwnerSmallId = sender.SmallId };
        }
    }

    /// <summary>
    /// Scene objects somebody has networked, by what names them in the level.
    /// Cleared with the world, since the next level has its own.
    /// </summary>
    private readonly Dictionary<(int Hash, int Index), FusionProtocol.PropCreate> _sceneProps = new();

    /// <summary>
    /// Guards the three caches below.
    ///
    /// They are written from the message loop and cleared from the panel and the
    /// console, which are their own threads. A Dictionary written from two at
    /// once corrupts rather than complains, so every touch takes this.
    /// </summary>
    private readonly object _cacheLock = new();

    /// <summary>
    /// What the caches will hold before they stop taking new entries.
    ///
    /// Everything in them comes from a message a client sent, keyed on numbers
    /// the client chose, so without a ceiling one player can make the server hold
    /// whatever they like for the life of the level, and make every future join
    /// carry it. A real level has tens of these, not thousands.
    /// </summary>
    private const int MaxCachedProps = 4096;

    /// <summary>The most of a message body worth keeping. A real one is tiny.</summary>
    private const int MaxCachedBody = 512;

    /// <summary>
    /// Constraints that exist, keyed on the first of their two ends, with the
    /// payload exactly as it was sent on. Replayed to a newcomer.
    /// </summary>
    private readonly Dictionary<ushort, (byte Owner, byte[] Payload)> _constraints = new();

    /// <summary>
    /// The last value of every RPC variable, by tag and by which variable it is.
    ///
    /// A level's own state lives in these: lights, gates, elevators, anything an
    /// SDK map wires up. A host replays them to a newcomer; nothing did here, so
    /// somebody joining saw the level in its default state while everybody else
    /// saw the real one. A prop's values go when the prop does.
    /// </summary>
    private readonly RpcVariableCache _rpcVariables = new();

    /// <summary>Which caches have already said they are full, so it is said once.</summary>
    private readonly HashSet<string> _cacheFull = new();

    /// <summary>Players already told they are at the limit, so it is said once.</summary>
    private readonly HashSet<byte> _unqueueRefused = new();

    private bool HasLevelVariables
    {
        get { lock (_cacheLock) { return _rpcVariables.Count > 0; } }
    }

    /// <summary>
    /// Tells a newcomer about every scene object already networked.
    ///
    /// The owner is rewritten to somebody who is still here. A client resolves
    /// the owner it is given and then asks that player for the object's state; an
    /// owner it cannot resolve gives it null, and the call it makes next
    /// dereferences that, so the state never arrives.
    /// </summary>
    private int SendSceneProps(ConnectedPlayer player)
    {
        int sent = 0;

        List<FusionProtocol.PropCreate> props;

        lock (_cacheLock)
        {
            props = _sceneProps.Values.ToList();
        }

        foreach (var prop in props)
        {
            // Gone from the world, so the id may already belong to something
            // else. Telling a newcomer about it would weld their copy of the
            // level to whatever holds that id now.
            if (Entities.Get(prop.EntityId) == null)
            {
                lock (_cacheLock)
                {
                    _sceneProps.Remove((prop.Hash, prop.Index));
                }

                continue;
            }

            byte owner = WorldCatchup.PropOwner(
                Entities.Get(prop.EntityId)?.OwnerSmallId,
                prop.OwnerSmallId,
                player.SmallId,
                id => Players.Get(id) != null,
                Players.Players.FirstOrDefault(p => p.SmallId != player.SmallId)?.SmallId);

            SendTo(player.Connection, FusionProtocol.BuildPropCreate(
                owner, prop.Hash, prop.Index, prop.EntityId), reliable: true);

            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Tells a newcomer about every constraint already in the world.
    ///
    /// The payload is the one that was broadcast, ids and all, so their copy
    /// agrees with everybody else's. Sent as the maker if they are still here,
    /// otherwise as somebody who is, since a client resolves the name it is given
    /// and cannot resolve one who has left.
    /// </summary>
    private int SendConstraints(ConnectedPlayer player)
    {
        int sent = 0;

        List<(ushort End, byte Owner, byte[] Payload)> welds;

        lock (_cacheLock)
        {
            welds = _constraints.Select(c => (c.Key, c.Value.Owner, c.Value.Payload)).ToList();
        }

        foreach (var weld in welds)
        {
            if (Entities.Get(weld.End) == null)
            {
                lock (_cacheLock)
                {
                    _constraints.Remove(weld.End);
                }

                continue;
            }

            byte from = Players.Get(weld.Owner) != null
                ? weld.Owner
                : Players.Players.FirstOrDefault(p => p.SmallId != player.SmallId)?.SmallId
                    ?? player.SmallId;

            SendTo(player.Connection, ModuleProtocol.WriteModuleToClients(
                ModuleProtocol.ConstraintCreateTag, from, weld.Payload), reliable: true);

            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Remembers the latest value of an RPC variable, then lets it carry on.
    /// </summary>
    private void NoteRpcVariable(byte tag, byte from, byte[] message)
    {
        byte[]? body = GateProtocol.TryReadBody(message, tag);

        if (body == null || GateProtocol.TryReadRpcPath(body) is not { } path)
        {
            return;
        }

        // Only the latest matters. A gate opened and closed forty times needs one
        // message to say which it is now.
        if (body.Length > MaxCachedBody)
        {
            return;
        }

        CacheRpcVariable(tag, from, body, path);
    }

    /// <summary>
    /// Holds an RPC variable's latest value, so somebody who joins later is told
    /// it. Split out because the server sets variables of its own, on behalf of
    /// plugins, and those have to be replayed the same way a client's are.
    /// </summary>
    private void CacheRpcVariable(byte tag, byte from, byte[] body, byte[] path)
    {
        lock (_cacheLock)
        {
            if (!_rpcVariables.Set(tag, from, body, path) && _cacheFull.Add("variables"))
            {
                Log("WARN", $"Holding {RpcVariableCache.MaxVariables} level variables and not taking " +
                            "more. A level does not have this many.");
            }
        }
    }

    /// <summary>
    /// Sends a player every RPC variable's current value.
    ///
    /// Not at join. These carry SkipHandleWhileLoading, which means a client
    /// throws them away rather than queueing them while it loads, so sending
    /// during the handshake would be sending them into nothing. This runs when
    /// the player says they have finished loading instead.
    /// </summary>
    private int SendRpcVariables(ConnectedPlayer player)
    {
        int sent = 0;

        List<(byte Tag, byte From, byte[] Body)> variables;

        lock (_cacheLock)
        {
            variables = _rpcVariables.All();
        }

        foreach (var (tag, from, body) in variables)
        {
            SendRpcVariable(player, tag, from, body);
            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Sends one held variable as the player who set it, and only while they are
    /// still here. A value stamped as the server would present whatever somebody put
    /// in the cache to every later joiner as though the level said it.
    /// </summary>
    private void SendRpcVariable(ConnectedPlayer player, byte tag, byte from, byte[] body)
    {
        byte source = Players.Get(from) != null ? from : player.SmallId;

        SendTo(player.Connection,
            GateProtocol.BuildRpcVariable(tag, player.SmallId, source, body), reliable: true);
    }

    private void HandleMetadataRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = FusionProtocol.TryReadMetadataRequest(message);

        if (request == null)
        {
            Log("WARN", $"PlayerMetadataRequest from {sender.DisplayName} did not parse");
            return;
        }

        // Player 0 is the server. Loading false on it would build it a body.
        if (request.Value.PlayerSmallId == PlayerRegistry.ServerSmallId)
        {
            Refuse(sender, "metadata", $"{sender.DisplayName} tried to set metadata on the server's player 0");
            return;
        }

        if (request.Value.PlayerSmallId != sender.SmallId
            && !sender.Permission.IsAtLeast(PermissionLevel.Operator))
        {
            Refuse(sender, "metadata", $"{sender.DisplayName} tried to set metadata on SmallID " +
                        $"{request.Value.PlayerSmallId}, which is not theirs");
            return;
        }

        // A nickname is metadata like any other, so handling metadata at all
        // opened a way around both nickname guards: the reserved names that stop
        // somebody calling themselves an operator, and the cap on how often a
        // name may change. Neither was reachable before, because this message was
        // dropped and a name could only be set at the handshake.
        if (string.Equals(request.Value.Key, "Nickname", StringComparison.OrdinalIgnoreCase)
            && Players.Get(request.Value.PlayerSmallId) is { } named)
        {
            var verdict = _nicknames.Allow(
                named.SmallId, request.Value.Value, DateTime.UtcNow);

            if (!verdict.Allowed)
            {
                if (verdict.Report)
                {
                    string more = verdict.Silenced > 0
                        ? $", and {verdict.Silenced} more since the last of these"
                        : "";

                    Log("WARN", $"{named.DisplayName} tried the nickname " +
                                $"'{request.Value.Value}': {verdict.Reason}{more}");
                }

                return;
            }

            // The panel and the lobby read this rather than the metadata, so it
            // showed the name they arrived with while everybody in game saw the
            // new one.
            named.Nickname = request.Value.Value;
        }

        // Kept as well as passed on. The join catch-up sends each player's
        // metadata, and it was only ever what they arrived with, so a nickname or
        // a mod's per-player state reverted for whoever joined next.
        if (Players.Get(request.Value.PlayerSmallId) is { } owner)
        {
            owner.SetMetadata(request.Value.Key, request.Value.Value);
        }

        Broadcast(FusionProtocol.BuildMetadataResponse(
            request.Value.PlayerSmallId, request.Value.Key, request.Value.Value), reliable: true);

        // A client says so here when it has finished loading the level, which is
        // the only moment it will accept the level's own state. Anything sent
        // before this was thrown away rather than queued.
        bool finishedLoading = WorldCatchup.FinishedLoading(request.Value.Key, request.Value.Value);

        if (finishedLoading
            && !sender.LevelStateSent
            && HasLevelVariables)
        {
            // Once each. They say when they are ready and the answer is the whole
            // level's state, so repeating the claim would be a cheap way to make
            // the server send it again and again.
            sender.LevelStateSent = true;

            int replayed = SendRpcVariables(sender);

            if (replayed > 0)
            {
                Log("INFO", $"{sender.DisplayName} finished loading, sent {replayed} " +
                            "level variables");
            }
        }

        // Holsters and magazines again. The sends after joining are thrown away by
        // a client still loading, and a slow machine is still loading at 9 s.
        if (finishedLoading
            && request.Value.PlayerSmallId == sender.SmallId
            && !sender.AttachmentsResent)
        {
            sender.AttachmentsResent = true;
            ReseatAttachments(sender, AfterLoadingDelays);
            ResendOwnVariables(sender, AfterLoadingDelays);
        }
    }

    private void HandleSpawnRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = FusionProtocol.TryReadSpawnRequest(message);

        if (request == null)
        {
            Refuse(sender, "spawn", $"SpawnRequest from {sender.DisplayName} did not parse");
            return;
        }

        // First, so a flood of requests that would all be refused is cut off before any other work.
        if (!_rateLimiter.Allow(sender.SmallId, DateTime.UtcNow))
        {
            Refuse(sender, "spawn", $"Spawn by {sender.DisplayName} denied: over the per-second rate cap");
            return;
        }

        // blocklist.json is reread when saved; server.json is not, so the exemptions
        // there can be tuned without a restart.
        var exempt = Config.SpawningExempt
            .Concat(Blocklist?.Current?.SpawnExempt ?? Enumerable.Empty<string>());

        var rankVerdict = SpawnAuthority.Check(
            sender.Permission, Config.Spawning, request.Value.Barcode, exempt,
            request.Value.Source, Config.SpawningExemptSources);

        if (rankVerdict.Blocked)
        {
            Refuse(sender, "spawn", $"Spawn of '{request.Value.Barcode}' by {sender.DisplayName} " +
                        $"denied: {rankVerdict.Reason} (source={request.Value.Source})");
            return;
        }

        var blockVerdict = _blocklist.Check(request.Value.Barcode, sender.Permission);

        if (blockVerdict.Blocked)
        {
            Refuse(sender, "spawn", $"Spawn of '{request.Value.Barcode}' by {sender.DisplayName} " +
                        $"denied by the {blockVerdict.Layer} blocklist: {blockVerdict.Reason}");
            return;
        }

        var toolVerdict = ToolGate.Check(request.Value.Barcode, sender.Permission, ToolGatesFromConfig());

        if (toolVerdict.Blocked)
        {
            Refuse(sender, "spawn", $"Spawn of '{request.Value.Barcode}' by {sender.DisplayName} " +
                        $"denied: {toolVerdict.Reason}");
            return;
        }

        var pluginSpawn = Plugins?.Spawn.Raise(new Plugins.SpawnEvent(
            sender.PlatformId, sender.DisplayName, sender.Permission,
            request.Value.Barcode, request.Value.Source));

        if (pluginSpawn is { Allowed: false })
        {
            Refuse(sender, "spawn", $"Spawn of '{request.Value.Barcode}' by {sender.DisplayName} " +
                        $"denied by a plugin: {pluginSpawn.Reason}");
            return;
        }

        if (Entities.SpawnedCount >= Config.MaxEntities)
        {
            // Make room from abandoned props rather than refusing. A refused spawn is
            // invisible to the player, they pull the trigger and nothing happens -
            // and once the world is full it stays full, so every spawn after that
            // fails for everyone.
            var evicted = Entities.EvictOldest(Config.EvictBatchSize, inUse: EntitiesInUse());

            if (evicted.Count > 0)
            {
                DespawnOnClients(evicted);
                Log("INFO", $"World at capacity, evicted {evicted.Count} abandoned entities");
            }

            if (Entities.SpawnedCount >= Config.MaxEntities)
            {
                // Nothing abandoned to take, so take the oldest thing there is.
                // A world that stays full refuses every spawn from every player
                // until somebody restarts the server, which is a worse outcome
                // than one stale prop going.
                // Only things that have sat still for two minutes. Anything in
                // use keeps sending poses, so a player's own work is never taken
                // out from under them, and somebody adding entities quickly
                // cannot aim the eviction at anybody else.
                var forced = Entities.EvictOldest(
                    Config.EvictBatchSize, anyOwner: true, idleFor: TimeSpan.FromMinutes(2),
                    inUse: EntitiesInUse());

                if (forced.Count > 0)
                {
                    DespawnOnClients(forced);
                    Log("WARN", $"World still at capacity with nothing abandoned, so the " +
                                $"{forced.Count} entities nobody has touched in two minutes " +
                                "were removed. Set IdleTimeoutSeconds so it does not come to this.");
                }
            }

            if (Entities.SpawnedCount >= Config.MaxEntities)
            {
                Refuse(sender, "spawn", $"Spawn denied: entity limit reached ({Config.MaxEntities}) " +
                            "and nothing could be evicted");
                return;
            }
        }

        // Only what they actually spawned. The last player standing inherits everyone
        // else's leftovers, one player was holding 1071 entities after a night of
        // this, and counting those would have the guard purge and eventually kick
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

        // While the spawner is still here to answer. Everyone else is about to be
        // told about this thing, and the ones who have not got it will go asking.
        AskWhereItComesFrom(sender, request.Value.Barcode);

        ushort entityId = Entities.AllocateId();

        var spawned = Entities.Register(entityId, request.Value.Barcode, sender.SmallId,
            request.Value.Position.X, request.Value.Position.Y, request.Value.Position.Z,
            request.Value.Rotation);

        // Echoed rather than chosen. A real host passes the request's own source
        // through, and it decides how a client treats the thing afterwards.
        //
        // Held to the three the enum has, unlike the host, because we keep it and
        // repeat it to everybody who joins for the rest of the level.
        spawned.Source = request.Value.Source <= FusionProtocol.SourcePlayer
            ? request.Value.Source
            : FusionProtocol.SourcePlayer;

        Broadcast(FusionProtocol.BuildSpawnResponse(sender.SmallId, sender.SmallId, entityId,
            request.Value.Barcode, request.Value.Position, request.Value.Rotation,
            request.Value.TrackerId, request.Value.SpawnEffect,
            source: request.Value.Source), reliable: true);

        // Source and effect are logged because they are what tells a reload apart
        // from a spawn menu, which a barcode alone does not.
        Log("SPAWN", $"id={entityId} '{request.Value.Barcode}' by {sender.DisplayName} " +
                    $"(source={request.Value.Source}, effect={request.Value.SpawnEffect})");
    }

    /// <summary>
    /// The spawn gun's delete mode sends a DespawnRequest, which is addressed to the
    /// server alone. Clients only remove an object when they receive a matching
    /// DespawnResponse, so the server has to answer or nothing ever disappears.
    /// </summary>
    /// <summary>
    /// Extended protection for message types that are otherwise relayed unread.
    /// Returns false when the message should be dropped instead of forwarded.
    /// </summary>
    private bool PassesGates(ConnectedPlayer sender, byte tag, byte[] message)
    {
        if (!Config.ExtendedProtection)
        {
            return true;
        }

        switch (tag)
        {
            case GateProtocol.TagPlayerRepDamage:
                float? damage = GateProtocol.TryReadDamage(message);

                if (damage is { } dealt && Config.MaxRemoteDamage > 0 && dealt > Config.MaxRemoteDamage)
                {
                    Log("WARN", $"{sender.DisplayName} sent {dealt:0} damage, over the " +
                                $"{Config.MaxRemoteDamage:0} cap, dropped");
                    return false;
                }

                return true;

            case GateProtocol.TagPlayerRepTeleport:
                if (!sender.Permission.IsAtLeast(Config.Teleportation))
                {
                    Log("WARN", $"{sender.DisplayName} tried to teleport someone but is " +
                                $"{sender.Permission.ToFusionString()}, not " +
                                $"{Config.Teleportation.ToFusionString()}, dropped");
                    return false;
                }

                return true;

            case GateProtocol.TagPlayerRepAvatar:
                string? barcode = GateProtocol.TryReadAvatarBarcode(message);

                if (barcode != null)
                {
                    var verdict = _blocklist.Check(barcode, sender.Permission);

                    if (verdict.Blocked)
                    {
                        Log("WARN", $"{sender.DisplayName} tried to wear '{barcode}', denied by " +
                                    $"the {verdict.Layer} blocklist: {verdict.Reason}");
                        return false;
                    }
                }

                return true;

            case GateProtocol.TagSlowMoButton:
                if (Config.SlowMoMode == 0)
                {
                    Log("WARN", $"{sender.DisplayName} pressed slow motion, which is disabled, dropped");
                    return false;
                }

                return true;

            default:
                return true;
        }
    }

    private void HandleDespawnRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = ServerProtocol.TryReadDespawnRequest(message);

        if (request == null)
        {
            Log("WARN", $"DespawnRequest from {sender.DisplayName} did not parse");
            return;
        }

        var (entityId, despawnEffect) = request.Value;

        if (Config.ExtendedProtection
            && Entities.Get(entityId) is { } target
            && !DespawnAuthority.MayDespawn(target.OwnerSmallId, sender.SmallId, sender.Permission))
        {
            Refuse(sender, "despawn", $"{sender.DisplayName} tried to despawn entity {entityId}, " +
                        "which belongs to someone else");
            return;
        }

        Entities.Remove(entityId);

        Broadcast(ServerProtocol.WriteDespawnResponse(sender.SmallId, entityId, despawnEffect),
            reliable: true);

        Log("INFO", $"Despawn: id={entityId} by {sender.DisplayName}");
    }

    /// <summary>Logs a refused request without letting a flood of them stall the server, and kicks whoever floods.</summary>
    private void Refuse(ConnectedPlayer sender, string kind, string line)
    {
        var verdict = _refusals.Note(sender.SmallId, kind, Clock());

        if (verdict.Log)
        {
            Log("WARN", verdict.Suppressed > 0 ? $"{line} ({verdict.Suppressed} more like it before this)" : line);
        }

        if (verdict.Kick)
        {
            Kick(sender.SmallId, "Flooding the server with refused requests");
        }
    }

    // ---- moderation ----

    public const string PermissionMetadataKey = "PermissionLevel";

    /// <summary>
    /// Handles a moderation command a player issued from the in-game menu. The client
    /// hides the buttons it thinks you may not use, but that is only a hint, the
    /// server is the thing that actually decides.
    /// </summary>
    private void HandlePermissionCommand(ConnectedPlayer sender, byte[] message)
    {
        var request = ServerProtocol.TryReadPermissionCommand(message);

        if (request == null)
        {
            Log("WARN", $"A moderation command from {sender.DisplayName} did not parse");
            return;
        }

        var (command, targetId) = request.Value;

        if (!targetId.HasValue)
        {
            Log("WARN", $"{sender.DisplayName} sent a {command} naming nobody");
            return;
        }

        if (targetId.Value == PlayerRegistry.ServerSmallId)
        {
            Log("WARN", $"{sender.DisplayName} tried to {command} SmallID 0, which is the server");
            return;
        }

        var target = Players.Get(targetId.Value);

        // Every one of these used to return in silence, so a moderator pressing
        // the button saw nothing happen and there was nothing in the log either.
        if (target == null)
        {
            Log("WARN", $"{sender.DisplayName} tried to {command} SmallID " +
                        $"{targetId.Value}, who is no longer connected");
            return;
        }

        if (target.SmallId == sender.SmallId)
        {
            Log("WARN", $"{sender.DisplayName} tried to {command} themselves");
            return;
        }

        void Deny(string action, PermissionLevel required)
        {
            var verdict = Moderation.Check(
                action, sender.Permission, target.Permission, required);

            Log("WARN", $"{sender.DisplayName} tried to {action} {target.DisplayName}: " +
                        $"{verdict.Reason}");
        }

        var pluginModeration = Plugins?.Moderation.Raise(new Plugins.ModerationEvent(
            sender.PlatformId, sender.DisplayName, target.PlatformId, command.ToString()));

        if (pluginModeration is { Allowed: false })
        {
            Log("WARN", $"{sender.DisplayName} tried to {command} {target.DisplayName}, " +
                        $"refused by a plugin: {pluginModeration.Reason}");
            return;
        }

        switch (command)
        {
            case ServerProtocol.PermissionCommand.Kick:
                if (!Moderation.Check("kick", sender.Permission, target.Permission,
                        Config.Kicking).Allowed)
                {
                    Deny("kick", Config.Kicking);
                    return;
                }

                Log("WARN", $"{sender.DisplayName} kicked {target.DisplayName}");
                Kick(target.SmallId, $"Kicked by {sender.DisplayName}");
                return;

            case ServerProtocol.PermissionCommand.Ban:
                if (!Moderation.Check("ban", sender.Permission, target.Permission,
                        Config.Banning).Allowed)
                {
                    Deny("ban", Config.Banning);
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

                if (!sender.HasPosition || !target.HasPosition)
                {
                    Log("WARN", $"{sender.DisplayName} tried to teleport {target.DisplayName} " +
                                "but one of them has not reported a position yet");
                    return;
                }

                // Relay drops anything addressed to the server, so passing the request
                // on did nothing at all. The server has to send the teleport itself.
                var plan = TeleportPlanner.For(command,
                    sender.SmallId, sender.LastPosition,
                    target.SmallId, target.LastPosition);

                if (plan is not { } move || Players.Get(move.MoveSmallId) is not { } moved)
                {
                    return;
                }

                SendTo(moved.Connection,
                    ServerProtocol.WritePlayerTeleport(sender.SmallId, move.To), reliable: true);

                Log("INFO", $"{sender.DisplayName} teleported {moved.DisplayName}");
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
    private readonly Dictionary<(byte Requester, uint Tracker), (string Barcode, DateTime Asked)>
        _pendingModInfo = new();

    /// <summary>Who has already been asked about a barcode, so nobody is asked twice.</summary>
    private readonly HashSet<(string Barcode, byte Holder)> _askedFor = new();

    /// <summary>Tracker ids for the server's own questions. Counts from one.</summary>
    private uint _modInfoTracker = 1;

    private const int MaxPendingModInfo = 512;
    private const int MaxAskedFor = 4096;
    private static readonly TimeSpan ModInfoPatience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A question nobody answers is never removed, so this drops the ones whose
    /// asker gave up long ago. Fusion waits five seconds; thirty is generous.
    /// </summary>
    private void PruneModInfo()
    {
        var cutoff = DateTime.UtcNow - ModInfoPatience;

        foreach (var key in _pendingModInfo
                     .Where(p => p.Value.Asked < cutoff)
                     .Select(p => p.Key)
                     .ToList())
        {
            _pendingModInfo.Remove(key);
        }

        if (_pendingModInfo.Count > MaxPendingModInfo)
        {
            foreach (var key in _pendingModInfo
                         .OrderBy(p => p.Value.Asked)
                         .Take(_pendingModInfo.Count - MaxPendingModInfo)
                         .Select(p => p.Key)
                         .ToList())
            {
                _pendingModInfo.Remove(key);
            }
        }
    }

    /// <summary>
    /// Asks a player where a barcode comes from, so the server can hand the answer
    /// to anybody else who needs it.
    ///
    /// A client that meets something it has not got asks whoever owns it, never the
    /// server, so none of that traffic taught the server anything. It then had
    /// nothing to say when somebody joined after the owner left, or when the owner
    /// installed the mod by hand and so had no mod.io listing to quote. Asking once,
    /// when a barcode first appears, fills the catalogue while the owner is still
    /// here.
    /// </summary>
    private void AskWhereItComesFrom(ConnectedPlayer holder, string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode) ||
            IsBaseGame(barcode) ||
            Config.FindMod(barcode) != null ||
            _askedFor.Count >= MaxAskedFor ||
            !_askedFor.Add((barcode, holder.SmallId)))
        {
            return;
        }

        PruneModInfo();

        uint tracker = _modInfoTracker++;
        _pendingModInfo[(PlayerRegistry.ServerSmallId, tracker)] = (barcode, DateTime.UtcNow);

        SendTo(holder.Connection,
            ServerProtocol.WriteModInfoRequest(holder.SmallId, barcode, tracker),
            reliable: true);
    }

    /// <summary>
    /// Content that ships with the game. Everybody already has it, and a client
    /// asked about one answers nothing because there is no mod.io listing behind
    /// it, so asking would fill the asked list with questions that never resolve.
    /// </summary>
    private static bool IsBaseGame(string barcode)
        => barcode.StartsWith("SLZ.", StringComparison.OrdinalIgnoreCase) ||
           barcode.StartsWith("fa534c5a868247138f50c62e424c4144.", StringComparison.OrdinalIgnoreCase) ||
           barcode.StartsWith("c3534c5a", StringComparison.OrdinalIgnoreCase);

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

        // A client asking another client, which is what a missing spawnable or
        // avatar produces. It still goes to whoever was asked, but the server
        // answers too when it knows: the owner returns in silence if the mod was
        // installed by hand rather than from mod.io, and the asker then waits out
        // its five second fuse with nothing to show. Fusion keeps only the first
        // reply, so a second one costs nothing.
        if (target.HasValue && target.Value != PlayerRegistry.ServerSmallId)
        {
            Relay(sender, message);

            if (Config.FindMod(barcode) is { } known)
            {
                SendTo(sender.Connection,
                    ServerProtocol.WriteModInfoResponse(sender.SmallId, known.ModId, known.ModFileId,
                        Config.ModPlatform, trackerId),
                    reliable: true);

                Log("INFO", $"{sender.DisplayName} asked {target.Value} about '{barcode}'; " +
                            $"answered from the catalogue as mod.io {known.ModId}");
            }
            else
            {
                Log("INFO", $"{sender.DisplayName} asked {target.Value} about '{barcode}', " +
                            "which the server has no id for");

                // Ask as well, so the next person who needs it is not left waiting
                // on somebody who may have gone by then.
                if (Players.Get(target.Value) is { } owner)
                {
                    AskWhereItComesFrom(owner, barcode);
                }
            }

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

        // The server owns no mods, so it cannot look this up itself, but whoever
        // spawned the item can. Hand the question to them; their reply is already
        // addressed back to the asker, and the server learns the answer in passing.
        byte? holder = FindHolder(barcode, except: sender.SmallId);

        if (holder.HasValue &&
            ServerProtocol.RetargetToTarget(ServerProtocol.StampSender(message, sender.SmallId), holder.Value)
                is { } forwarded &&
            Players.Get(holder.Value) is { } holderPlayer)
        {
            PruneModInfo();
            _pendingModInfo[(sender.SmallId, trackerId)] = (barcode, DateTime.UtcNow);

            SendTo(holderPlayer.Connection, forwarded, reliable: true);

            Log("INFO", $"Asked {holderPlayer.DisplayName} where '{barcode}' comes from, " +
                        $"for {sender.DisplayName}");
            return;
        }

        Log("WARN", $"{sender.DisplayName} asked for mod info on '{barcode}', " +
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
    /// players use. Once learned, a barcode can be served directly, including to
    /// someone who joins long after the original owner left.
    /// </summary>
    private void HandleModInfoResponse(ConnectedPlayer sender, byte[] message)
    {
        var response = ServerProtocol.TryReadModInfoResponse(message);

        if (response is { } r && r.Target.HasValue &&
            _pendingModInfo.Remove((r.Target.Value, r.TrackerId), out var pending) &&
            r.ModId > 0)
        {
            RememberHolder(pending.Barcode, sender.SmallId);

            if (Config.LearnMod(pending.Barcode, r.ModId, r.ModFileId))
            {
                Config.Save(Program.ConfigPath);
                Log("INFO", $"Learned '{pending.Barcode}' is mod.io {r.ModId}, " +
                            "the server can serve it from now on");
            }

            // The server asked this one itself, so there is nobody to pass it to.
            if (r.Target.Value == PlayerRegistry.ServerSmallId)
            {
                return;
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
    /// <summary>
    /// Sends a module message to one player, stamped as coming from the server.
    /// Some mods refuse anything whose sender is not the server, so the small id
    /// is not the plugin's to choose.
    /// </summary>
    public void SendModuleTo(ulong platformId, long handlerTag, byte[] payload)
    {
        if (Players.GetByPlatformId(platformId) is not { } target)
        {
            return;
        }

        SendTo(target.Connection, ModuleProtocol.WriteModuleToClients(
            handlerTag, PlayerRegistry.ServerSmallId, payload), reliable: true);
    }

    /// <summary>Sends a module message to everybody, stamped as from the server.</summary>
    public void BroadcastModule(long handlerTag, byte[] payload)
        => Broadcast(ModuleProtocol.WriteModuleToClients(
            handlerTag, PlayerRegistry.ServerSmallId, payload), reliable: true);

    /// <summary>
    /// Puts this level's placed props in front of somebody who just joined.
    ///
    /// They are registered once, the first time anybody needs them, and sent to
    /// everyone after that, so every client agrees on the ids. They are registered
    /// ownerless; the catch-up below then adopts them to an online player like any
    /// other ownerless entity, and being persistent is what stops a cull taking them.
    /// </summary>
    /// <summary>
    /// Puts the world in front of somebody who has just arrived.
    ///
    /// A relay is not a host, and Fusion's own catch-up runs on the host alone,
    /// so nothing tells a joining player what already exists. Every entity we
    /// know about is sent as the spawn it originally was, with its own ID and its
    /// original owner, so the newcomer agrees with everybody else about which
    /// entity is which.
    ///
    /// No spawn effect, because these are not being spawned now. Once a client
    /// knows an entity exists it asks that entity's owner for the rest, which is
    /// how a holstered gun ends up in the right hand rather than on the floor.
    /// </summary>
    /// <returns>How many were sent.</returns>
    private int SendWorldCatchup(ConnectedPlayer player)
    {
        var replay = WorldCatchup.For(Entities.Entities);

        int sent = 0;

        foreach (var entity in replay)
        {
            // An owner every client will agree on. A client registers what it is
            // told it owns with its own update loop and starts sending poses for
            // it, so naming the person being caught up meant each new arrival
            // took ownership of every ownerless prop and they all simulated the
            // same object against each other. That is what flinging looks like.
            //
            // Adopting rather than naming one for this message alone, so the next
            // person told about it hears the same answer.
            byte owner = entity.OwnerSmallId ?? Adopt(entity, player);

            // Not tracker zero. A client counts its own spawn trackers up from
            // zero, and a catch-up naming a tracker it is waiting on would fire
            // that callback with the wrong thing. Nothing counts this high.
            SendTo(player.Connection, FusionProtocol.BuildSpawnResponse(
                owner, owner,
                entity.Id, entity.Barcode,
                new Vec3(entity.X, entity.Y, entity.Z), entity.Rotation,
                CatchupTracker,
                spawnEffect: false, source: entity.Source), reliable: true);

            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Gives an ownerless entity to somebody, once, so every catch-up after this
    /// names the same person. The longest-standing player rather than the newest,
    /// because they are the least likely to leave next.
    /// </summary>
    private byte Adopt(TrackedEntity entity, ConnectedPlayer joining)
    {
        // Somebody who is already here, so the newcomer is never told it owns
        // something it is only being introduced to. A real host never does that,
        // and on the receiving client it also fires the spawn callback for
        // tracker 0, which is a real tracker number somebody may be waiting on.
        byte owner = Players.Players
            .Where(p => p.SmallId != joining.SmallId)
            .Select(p => (byte?)p.SmallId)
            .FirstOrDefault() ?? joining.SmallId;

        Entities.SetOwner(entity.Id, owner);
        AnnounceOwner(entity.Id, owner);

        return owner;
    }

    /// <summary>
    /// Registers this level's placed props, once, the first time anybody needs them.
    ///
    /// Only registering. The catch-up above sends every entity it knows about,
    /// props included, so sending them here as well put each prop on a client
    /// twice. They are registered ownerless; the catch-up adopts each one to an
    /// online player, and being persistent is what stops a cull taking them.
    /// </summary>
    private void EnsurePersistentProps(ConnectedPlayer player)
    {
        if (Props is not { } store)
        {
            return;
        }

        var claimed = new HashSet<ushort>();

        foreach (var prop in store.For(Config.LevelBarcode))
        {
            // Props side by side can both be in range of one entity, so each record takes the closest one not yet taken.
            var existing = Entities.Entities
                .Where(e => e.Persistent
                    && !claimed.Contains(e.Id)
                    && string.Equals(e.Barcode, prop.Barcode, StringComparison.OrdinalIgnoreCase)
                    && MatchesKeptPosition(e, prop))
                .OrderBy(e => KeptDistanceSquared(e, prop))
                .FirstOrDefault();

            ushort id;

            if (existing != null)
            {
                id = existing.Id;
            }
            else
            {
                id = Entities.AllocateId();

                var tracked = Entities.Register(id, prop.Barcode, player.SmallId,
                    prop.X, prop.Y, prop.Z, prop.RotationBytes());

                tracked.Persistent = true;
                tracked.KeptAt = (prop.X, prop.Y, prop.Z);
                Entities.SetOwner(id, null);
            }

            claimed.Add(id);
        }
    }

    /// <summary>
    /// Matches a kept record against where the entity was kept, not where it has
    /// since settled to, falling back to its live position for one with no record.
    /// </summary>
    private static bool MatchesKeptPosition(TrackedEntity entity, Props.PersistentProp prop)
    {
        var (x, y, z) = entity.KeptAt ?? (entity.X, entity.Y, entity.Z);

        return Math.Abs(x - prop.X) < 0.5f
            && Math.Abs(y - prop.Y) < 0.5f
            && Math.Abs(z - prop.Z) < 0.5f;
    }

    private static float KeptDistanceSquared(TrackedEntity entity, Props.PersistentProp prop)
    {
        var (x, y, z) = entity.KeptAt ?? (entity.X, entity.Y, entity.Z);

        return (x - prop.X) * (x - prop.X) + (y - prop.Y) * (y - prop.Y) + (z - prop.Z) * (z - prop.Z);
    }

    /// <summary>Marks a tracked entity to be put back after a restart.</summary>
    public bool KeepProp(ushort entityId, string note)
    {
        if (Props is not { } store || Entities.Get(entityId) is not { } entity)
        {
            return false;
        }

        // The ends of a constraint are tracked under a barcode the server made
        // up. Writing one to the file would put it back on every restart as a
        // spawn of something no pallet has, and the entity that came back would
        // not be marked synthetic, so it would reach the join catch-up too.
        if (entity.Synthetic)
        {
            Log("WARN", $"'{entity.ShortName}' is part of a constraint, not a prop, " +
                        "so it cannot be kept");
            return false;
        }

        entity.Persistent = true;
        entity.KeptAt = (entity.X, entity.Y, entity.Z);

        store.Add(new Props.PersistentProp
        {
            Barcode = entity.Barcode,
            Level = Config.LevelBarcode,
            X = entity.X,
            Y = entity.Y,
            Z = entity.Z,
            Rotation = Convert.ToHexString(entity.Rotation),
            Note = note,
        });

        store.Save();

        // Ownerless from here on. The next join catch-up adopts it to an online
        // player, the same as any other ownerless entity.
        Entities.SetOwner(entityId, null);

        Log("INFO", $"'{entity.ShortName}' will be put back on {Config.LevelTitle}");
        return true;
    }

    /// <summary>
    /// Stops putting a prop back, and lets the culls have it again. Returns false
    /// for an entity that does not exist or is not kept.
    /// </summary>
    public bool ForgetProp(ushort entityId)
    {
        if (Props is not { } store || Entities.Get(entityId) is not { Persistent: true } entity)
        {
            return false;
        }

        entity.Persistent = false;

        var (x, y, z) = entity.KeptAt ?? (entity.X, entity.Y, entity.Z);
        entity.KeptAt = null;

        if (!store.Remove(entity.Barcode, Config.LevelBarcode, x, y, z))
        {
            Log("WARN", $"'{entity.ShortName}' had no record where it was kept, so none was removed");
        }

        store.Save();

        Log("INFO", $"'{entity.ShortName}' will not be put back");
        return true;
    }

    /// <summary>
    /// Brings everybody to one player. For gathering a server up before a round,
    /// or getting people out of somewhere they have fallen into.
    ///
    /// A player who has not reported a position yet is left where they are, since
    /// there is nothing to send them to and moving them to the origin would drop
    /// them through the level.
    /// </summary>
    /// <returns>How many were moved, and how many were skipped.</returns>
    public (int Moved, int Skipped) GatherEveryoneTo(byte targetSmallId)
    {
        if (Players.Get(targetSmallId) is not { } target || !target.HasPosition)
        {
            return (0, 0);
        }

        int moved = 0;
        int skipped = 0;

        foreach (var player in Players.Players)
        {
            if (player.SmallId == targetSmallId)
            {
                continue;
            }

            if (!player.HasPosition)
            {
                skipped++;
                continue;
            }

            SendTo(player.Connection,
                ServerProtocol.WritePlayerTeleport(targetSmallId, target.LastPosition),
                reliable: true);

            moved++;
        }

        Log("WARN", $"Everyone was brought to {target.DisplayName}: {moved} moved" +
                    (skipped > 0 ? $", {skipped} had no position yet" : ""));

        return (moved, skipped);
    }

    /// <summary>
    /// Removes one entity for a plugin, the way the panel's Remove does: a kept prop
    /// stops being kept first, or the next restart puts it straight back.
    /// </summary>
    public bool RemoveEntity(ushort entityId)
    {
        if (Entities.Get(entityId) is { Persistent: true })
        {
            ForgetProp(entityId);
        }

        return DespawnEntity(entityId);
    }

    /// <summary>Removes one entity and tells the clients. For plugins.</summary>
    public bool DespawnEntity(ushort entityId)
    {
        if (!Entities.Remove(entityId))
        {
            return false;
        }

        DespawnOnClients(new[] { entityId });
        return true;
    }

    /// <summary>
    /// Puts a crate into the world for a plugin, announced the way the join catch-up
    /// announces a kept prop. With nobody connected it is only registered, and the
    /// catch-up tells whoever joins.
    /// </summary>
    /// <returns>The new entity id, or 0 when the barcode is empty.</returns>
    public ushort SpawnForPlugin(string barcode, float x, float y, float z, byte[] rotation)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return 0;
        }

        ushort id = Entities.AllocateId();
        byte? owner = Players.Players.FirstOrDefault()?.SmallId;

        Entities.Register(id, barcode, owner ?? 0, x, y, z, rotation);
        Entities.SetOwner(id, owner);

        if (owner is { } named)
        {
            Broadcast(FusionProtocol.BuildSpawnResponse(named, named, id, barcode,
                new Vec3(x, y, z), rotation, CatchupTracker, spawnEffect: false), reliable: true);
        }

        Log("SPAWN", $"id={id} '{barcode}' by a plugin");
        return id;
    }

    public int PurgeEntitiesOf(byte smallId)
    {
        // If the owner has already gone, name someone who is still here, see
        // Their own spawns only, sweeping up inherited props would delete the work of
        // players who have since left.
        var doomed = Entities.Entities
            .Where(e => e.OwnerSmallId == smallId && !e.Inherited)
            .Select(e => e.Id)
            .ToList();

        foreach (ushort id in doomed)
        {
            Entities.Remove(id);
        }

        DespawnOnClients(doomed);

        return doomed.Count;
    }

    /// <summary>
    /// Wipes the world.
    ///
    /// The despawn is attributed to a connected player rather than to the relay's own
    /// ID. A dedicated server never announces itself as a player, so small ID 0 names
    /// nobody the clients know about, and a despawn from a sender they cannot resolve
    /// is not acted on, the entity would disappear from the server's books while
    /// staying in everyone's world.
    /// </summary>
    public int ClearAllEntities(bool includeDiscovered = false)
    {
        // Clear() decides what is eligible; discovered entities are held back unless
        // asked for, because one may be a scene prop rather than a spawn.
        var doomed = Entities.Clear(includeDiscovered);

        DespawnOnClients(doomed);

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

        // Everything goes, props included. Anything placed on the new level is put
        // back as players arrive on it.
        Entities.Forget();

        // Clients send no releases as the scene unloads, and a grab on an id the
        // registry never knew is not cleared when the entities go.
        _grabs.Clear();

        lock (_cacheLock)
        {
            _sceneProps.Clear();
            _constraints.Clear();
            _rpcVariables.Clear();
            _cacheFull.Clear();
        }

        // A new level means new state, so everybody is owed it again.
        foreach (var player in Players.Players)
        {
            player.LevelStateSent = false;
            player.AttachmentsResent = false;
        }

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
        Log("WARN", $"Restart requested, disconnecting {Players.Count} players");

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

        Ranks?.Set(platformId, username, level);
        Ranks?.Save();

        var player = Players.GetByPlatformId(platformId);

        if (player != null)
        {
            player.Permission = level;
            player.SetMetadata(PermissionMetadataKey, level.ToFusionString());

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
        => Ban(platformId, username, reason, null, AuditChannel.Panel);

    public void Ban(ulong platformId, string username, string reason,
        TimeSpan? duration, AuditChannel channel, string actor = "", string note = "")
    {
        AuditTrail?.Record(channel, duration is null ? "ban" : "tempban", username, platformId,
            reason, actor);
        BanInternal(platformId, username, reason, duration, note);
    }

    private void BanInternal(ulong platformId, string username, string reason, TimeSpan? duration,
        string note = "")
    {
        if (BanList is { } list)
        {
            list.Ban(platformId, username, reason, duration, note);
            list.Save();
        }
        else
        {
            Config.Ban(platformId, username, reason);
        }

        var player = Players.GetByPlatformId(platformId);

        if (player != null)
        {
            Kick(player.SmallId, reason);
        }

        Log("WARN", $"Banned {(string.IsNullOrWhiteSpace(username) ? platformId.ToString() : username)}: {reason}");
    }

    public void MutePlayer(ulong platformId, string name)
    {
        if (Mutes.Mute(platformId))
        {
            AuditTrail?.Record(AuditChannel.Console, "mute", name, platformId, "");
            Log("WARN", $"Muted {(string.IsNullOrWhiteSpace(name) ? platformId.ToString() : name)}");
        }
    }

    public void UnmutePlayer(ulong platformId, string name)
    {
        if (Mutes.Unmute(platformId))
        {
            AuditTrail?.Record(AuditChannel.Console, "unmute", name, platformId, "");
            Log("INFO", $"Unmuted {(string.IsNullOrWhiteSpace(name) ? platformId.ToString() : name)}");
        }
    }

    public bool Unban(ulong platformId)
    {
        bool removed = BanList is { } list && list.Unban(platformId);

        if (removed)
        {
            BanList!.Save();
        }

        // A ban made before bans.json existed still lives in the config.
        removed |= Config.Unban(platformId);

        if (!removed)
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

    /// <summary>Last mortality warning said, so it is not repeated every tick.</summary>
    private string? _mortalityWarning;

    /// <summary>The name last sent for player 0, so a rename in the panel reaches connected players.</summary>
    private string? _serverPlayerName;

    /// <summary>
    /// Pushes the current settings to everyone connected. Without this a change made
    /// in the panel would only reach players who join afterwards.
    /// </summary>
    public void PushSettings()
    {
        Players.MaxPlayers = Config.MaxPlayers;

        RebuildBlocklist();

        string? unkillable = MortalityCheck.WhyUnkillable(Config.Mortality, Config.Knockout);

        if (unkillable != _mortalityWarning)
        {
            _mortalityWarning = unkillable;

            if (unkillable != null)
            {
                Log("WARN", unkillable);
            }
        }

        Broadcast(ServerProtocol.WriteServerSettings(BuildLobbyInfoJson()), reliable: true);

        // Player 0 carries the server's name. Stored only when it is sent, so a rename
        // from the panel thread cannot be recorded without reaching players.
        string serverName = Config.ServerName;

        if (_serverPlayerName != serverName)
        {
            _serverPlayerName = serverName;

            Broadcast(FusionProtocol.BuildMetadataResponse(
                PlayerRegistry.ServerSmallId, "Username", serverName), reliable: true);
        }
    }

    private ToolGates ToolGatesFromConfig()
        => new(Config.DevTools, Config.Constrainer, Config.Nimbus);

    /// <summary>
    /// Whether a player may take a tool that already exists. Taking it away is the
    /// only answer that holds, because refusing ownership stops the tool syncing but
    /// the client holding it still runs it, which is how a nimbus gun keeps flying.
    ///
    /// Only entities the server saw spawned have a barcode, so a tool placed in the
    /// level itself is not something this can recognise.
    /// </summary>
    private bool MayHold(ConnectedPlayer sender, ushort entityId)
    {
        var entity = Entities.Get(entityId);

        if (entity == null || string.IsNullOrEmpty(entity.Barcode))
        {
            return true;
        }

        var pluginTool = Plugins?.Tool.Raise(new Plugins.ToolEvent(
            sender.PlatformId, sender.DisplayName, sender.Permission, entity.Barcode, entityId));

        if (pluginTool is { Allowed: false })
        {
            Log("WARN", $"{sender.DisplayName} took '{entity.ShortName}' against a plugin: " +
                        $"{pluginTool.Reason}, removing it");

            Entities.Remove(entityId);
            DespawnOnClients(new[] { entityId });

            return false;
        }

        var verdict = ToolGate.Check(entity.Barcode, sender.Permission, ToolGatesFromConfig());

        if (!verdict.Blocked)
        {
            return true;
        }

        Log("WARN", $"{sender.DisplayName} took '{entity.ShortName}' without the rank for it " +
                    $"({verdict.Reason}), removing it");

        Entities.Remove(entityId);
        DespawnOnClients(new[] { entityId });

        return false;
    }

    /// <summary>Module handler tags already reported, so each is said once.</summary>
    private readonly HashSet<long> _unknownModules = new();

    /// <summary>
    /// Connections being torn down. Nothing they send is listened to, including a
    /// fresh ConnectionRequest, which is the only message that does not need a
    /// known sender and would otherwise let a kicked client back in during the
    /// quarter second before the socket closes.
    /// </summary>
    private readonly HashSet<uint> _closing = new();

    /// <summary>Host-only tags a client has tried to send, so it is said once.</summary>
    private readonly HashSet<byte> _forgedTags = new();

    private readonly object _closingLock = new();

    /// <summary>
    /// Module messages carry whatever Fusion's own modules define. The only one the
    /// server has to act on is a constraint, which needs the host to hand out an
    /// entity id for each end before anybody can build it.
    /// </summary>
    private void HandleModuleMessage(ConnectedPlayer sender, byte[] message)
    {
        long? handler = ModuleProtocol.TryReadHandlerTag(message);

        if (handler == null)
        {
            return;
        }

        // A plugin sees the message first, and Forward is how it hands control
        // back, so a plugin that only watches constraints does not stop them.
        if (PluginModules is { } modules && modules.Claims(handler.Value))
        {
            var action = modules.Dispatch(new FusionDedicated.Plugins.ModuleRequest(
                sender.PlatformId, sender.DisplayName, sender.Permission, handler.Value,
                ModuleProtocol.TryReadHandlerPayload(message) ?? Array.Empty<byte>()));

            switch (action.Kind)
            {
                case FusionDedicated.Plugins.ModuleActionKind.Drop:
                    return;

                case FusionDedicated.Plugins.ModuleActionKind.Rewrite:
                    Broadcast(ModuleProtocol.WriteModuleToClients(
                        handler.Value, sender.SmallId, action.Payload), reliable: true);
                    return;

                case FusionDedicated.Plugins.ModuleActionKind.Reply:
                    SendTo(sender.Connection, ModuleProtocol.WriteModuleToClients(
                        handler.Value, sender.SmallId, action.Payload), reliable: true);
                    return;
            }
        }

        if (handler == ModuleProtocol.ConstraintDeleteTag)
        {
            HandleConstraintDelete(sender, message);
            return;
        }

        // Watched, not acted on: these still go on to everybody afterwards.
        NoteAttachment(sender, handler.Value, message);

        if (handler != ModuleProtocol.ConstraintCreateTag)
        {
            if (_unknownModules.Add(handler.Value))
            {
                Log("INFO", $"Module message {handler.Value} is not handled here, passing it on");
            }

            // The tag alone names the door. This is what shows what came through
            // it, which is what writing a plugin to host that mod needs.
            ModuleInspector.Note(handler.Value, sender.SmallId, sender.DisplayName,
                ModuleProtocol.TryReadHandlerPayload(message) ?? Array.Empty<byte>());

            // Passed on rather than dropped. A module message is one mod talking
            // to the same mod on another client, and Fusion's own relay forwards
            // it unless a handler says otherwise. Swallowing them here broke
            // every client mod that speaks over modules, and clearing a
            // constraint with it: the delete never left the person who sent it.
            Relay(sender, message);
            return;
        }

        HandleConstraintCreate(sender, message);
    }

    /// <summary>
    /// Shows an RPC to whatever plugins are watching, and says whether it should
    /// still go on to the other clients.
    /// </summary>
    private FusionDedicated.Plugins.RpcActionKind OfferRpcToPlugins(ConnectedPlayer sender, byte tag, byte[] message)
    {
        if (PluginRpc is not { Watched: true } rpc)
        {
            return FusionDedicated.Plugins.RpcActionKind.Pass;
        }

        byte[]? body = GateProtocol.TryReadBody(message, tag);

        if (body == null || RpcProtocol.TryReadPath(body) is not { } path)
        {
            return FusionDedicated.Plugins.RpcActionKind.Pass;
        }

        var kind = RpcProtocol.KindOf(tag);

        return rpc.Dispatch(new FusionDedicated.Plugins.RpcRequest(
            sender.PlatformId, sender.DisplayName, sender.Permission, kind,
            path.Key, path.HasEntity, path.EntityId, path.ComponentIndex,
            RpcProtocol.ReadValue(kind, body))).Kind;
    }

    /// <summary>
    /// Sends an RPC as a plugin asked, to one player or to everybody.
    ///
    /// Stamped as coming from the server. Nothing on the receiving side checks who
    /// sent an RPC, so this is honest rather than necessary: the value really is
    /// the server's, unlike the ones replayed from the cache.
    /// </summary>
    public void SendRpc(BonelabServerBrowser.Fusion.RpcKind kind, string path,
        BonelabServerBrowser.Fusion.RpcValue value, ulong? platformId)
    {
        byte[] pathBytes;

        try
        {
            pathBytes = Convert.FromHexString(path);
        }
        catch (FormatException)
        {
            Log("WARN", $"A plugin asked to set an RPC on '{path}', which is not a path");
            return;
        }

        byte[] payload = RpcProtocol.WriteValue(kind, pathBytes, value);

        // Held so somebody who joins afterwards is told it too. An unchanged
        // broadcast is skipped, so a plugin undoes a per-player value with another per-player send.
        if (platformId == null
            && kind != BonelabServerBrowser.Fusion.RpcKind.Event
            && payload.Length <= MaxCachedBody)
        {
            lock (_cacheLock)
            {
                if (_rpcVariables.IsUnchanged((byte)kind, payload, pathBytes))
                {
                    return;
                }

                CacheRpcVariable((byte)kind, PlayerRegistry.ServerSmallId, payload, pathBytes);
            }
        }

        // Stamped as the player being told rather than as the server, which is
        // what the level variable replay does and what clients are known to
        // accept. The server has no player of its own on a relay, and a small ID
        // naming nobody is the one difference between this and the path that
        // works.
        if (platformId is { } who)
        {
            if (Players.GetByPlatformId(who) is { } target)
            {
                SendTo(target.Connection, GateProtocol.BuildRpcVariable(
                    (byte)kind, target.SmallId, target.SmallId, payload), reliable: true);
            }

            return;
        }

        foreach (var player in Players.Players)
        {
            SendTo(player.Connection, GateProtocol.BuildRpcVariable(
                (byte)kind, player.SmallId, player.SmallId, payload), reliable: true);
        }
    }

    /// <summary>
    /// One entity, for a plugin that needs to look one up. Null when it has gone.
    /// </summary>
    public FusionDedicated.Plugins.PluginEntity? FindEntity(ushort entityId)
    {
        if (Entities.Get(entityId) is not { } entity)
        {
            return null;
        }

        ulong owner = entity.OwnerSmallId is { } small
            ? Players.Get(small)?.PlatformId ?? 0UL
            : 0UL;

        return new FusionDedicated.Plugins.PluginEntity(
            entity.Id, entity.Barcode, owner, entity.X, entity.Y, entity.Z, entity.Persistent);
    }

    /// <summary>Rotation and velocity of one entity, for a plugin. Null when it has gone.</summary>
    public FusionDedicated.Plugins.PluginMotion? FindMotion(ushort entityId)
        => Entities.Get(entityId) is { } entity
            ? new FusionDedicated.Plugins.PluginMotion(
                entity.Rotation, entity.VelocityX, entity.VelocityY, entity.VelocityZ, entity.LastUpdate)
            : null;

    /// <summary>
    /// Everything in the world, for a plugin that has to find its own props.
    ///
    /// A prop cannot announce itself reliably. Anything it fires as it comes into
    /// the world leaves before Fusion has given it a network entity, so it
    /// arrives naming no entity and cannot be answered. Reading the world instead
    /// is the only way a plugin can find what it owns.
    /// </summary>
    public IReadOnlyList<FusionDedicated.Plugins.PluginEntity> AllEntities()
        => Entities.Entities
            .Select(e => new FusionDedicated.Plugins.PluginEntity(
                e.Id,
                e.Barcode,
                e.OwnerSmallId is { } small ? Players.Get(small)?.PlatformId ?? 0UL : 0UL,
                e.X, e.Y, e.Z, e.Persistent))
            .ToList();

    /// <summary>Gives one entity to one player, for a plugin. False when either is missing.</summary>
    public bool GiveOwner(ushort entityId, ulong platformId)
    {
        if (Entities.Get(entityId) is not { })
        {
            return false;
        }

        if (Players.GetByPlatformId(platformId) is not { } player)
        {
            return false;
        }

        Entities.SetOwner(entityId, player.SmallId);
        AnnounceOwner(entityId, player.SmallId);

        return true;
    }

    /// <summary>What each player has in their hands.</summary>
    private readonly GrabBook _grabs = new();

    /// <summary>Who holds an entity, earliest grab first.</summary>
    public IReadOnlyList<byte> HoldersOf(ushort entityId) => _grabs.HoldersOf(entityId);

    /// <summary>What players are holding or have holstered, for the cull and the eviction to leave alone.</summary>
    private HashSet<ushort> EntitiesInUse()
    {
        List<ushort> holstered;
        List<(ushort Magazine, ushort Gun)> loaded;

        lock (_cacheLock)
        {
            holstered = _slotted.All().Select(s => s.Weapon).ToList();
            loaded = _loaded.Select(m => (m.Key, m.Value)).ToList();
        }

        return InUse.Of(_grabs.All().Select(h => h.EntityId), holstered, loaded);
    }

    /// <summary>Which weapon is in which body slot, so a drop knows what left.</summary>
    private readonly HolsterSlots _slotted = new();

    /// <summary>Which gun each magazine is in, so a newcomer can be told.</summary>
    private readonly Dictionary<ushort, ushort> _loaded = new();

    /// <summary>A magazine each for a great many guns.</summary>
    private const int MaxSlotsTracked = 2048;

    /// <summary>Work waiting on a clock, drained by the main loop.</summary>
    private readonly List<(DateTime Due, Action Work)> _deferred = new();

    private readonly object _deferredLock = new();

    /// <summary>What deferred work counts time by. Tests set it to step through delays.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private void Defer(TimeSpan delay, Action work)
    {
        lock (_deferredLock)
        {
            _deferred.Add((Clock() + delay, work));
        }
    }

    /// <summary>
    /// Runs anything whose time has come. Called every pass of the main loop, so
    /// this is the only place with a clock finer than the ten second tick.
    /// </summary>
    public void PumpDeferred()
    {
        List<Action> due;

        lock (_deferredLock)
        {
            if (_deferred.Count == 0)
            {
                return;
            }

            var now = Clock();

            due = _deferred.Where(d => d.Due <= now).Select(d => d.Work).ToList();
            _deferred.RemoveAll(d => d.Due <= now);
        }

        foreach (var work in due)
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                Log("ERROR", $"Deferred work failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Everything the catch-up spawn could not say, for somebody who has just
    /// joined: holstered guns back on hips, magazines back in guns, and which
    /// props their owner has stopped simulating.
    ///
    /// All three are things that stopped sending pose updates, so the catch-up
    /// can only place them where they were last simulated, which is usually
    /// mid-air, and nothing moves them afterwards either. The cull status is the
    /// one that lets the newcomer fix it themselves: told that nobody is
    /// simulating a prop, their client takes it over when they walk up to it and
    /// it falls. Announced once when it happens, so anybody arriving later never
    /// hears it.
    ///
    /// A real host replays all of this through its own entity catch-up, which
    /// runs on the host alone and so never runs here.
    ///
    /// Sent late rather than with the rest of the catch-up: the client builds its
    /// entities from the spawn responses asynchronously, and a message naming an
    /// entity it has not finished making is dropped without a word. Twice, because
    /// how long that takes depends on the machine.
    /// </summary>
    private void ReseatAttachments(ConnectedPlayer player)
        => ReseatAttachments(player, AfterJoinDelays);

    private static readonly TimeSpan[] AfterJoinDelays = { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9) };

    /// <summary>
    /// After loading. Spawns wait for the level and are then built over a moment,
    /// so these are not sent the instant loading ends either.
    /// </summary>
    private static readonly TimeSpan[] AfterLoadingDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6) };

    private void ReseatAttachments(ConnectedPlayer player, TimeSpan[] delays)
    {
        foreach (var delay in delays)
        {
            Defer(delay, () =>
            {
                if (Players.Get(player.SmallId) != player)
                {
                    return;
                }

                int sent = SendAttachments(player);

                if (sent > 0)
                {
                    Log("INFO", $"Re-seated {sent} attachment(s) for {player.DisplayName}");
                }
            });
        }
    }

    /// <summary>
    /// Values on props the player owns. After a restart the first player owns every
    /// kept prop, and their client never asks about those, so a payphone showed no number.
    /// </summary>
    private void ResendOwnVariables(ConnectedPlayer player, TimeSpan[] delays)
    {
        foreach (var delay in delays)
        {
            Defer(delay, () =>
            {
                if (Players.Get(player.SmallId) != player)
                {
                    return;
                }

                var owned = WorldCatchup.OwnedBy(
                    Entities.Entities.Select(e => (e.Id, e.OwnerSmallId)), player.SmallId);

                foreach (ushort entityId in owned)
                {
                    ReplayVariables(player, entityId);
                }
            });
        }
    }

    /// <summary>Takes a removed prop off the holster and magazine books, as a holster or as what was in one.</summary>
    private void ForgetAttachments(ushort entityId)
    {
        lock (_cacheLock)
        {
            _slotted.ForgetEntity(entityId);

            foreach (ushort magazine in _loaded
                         .Where(m => m.Key == entityId || m.Value == entityId)
                         .Select(m => m.Key)
                         .ToList())
            {
                _loaded.Remove(magazine);
            }
        }
    }

    /// <returns>How many were sent.</returns>
    private int SendAttachments(ConnectedPlayer player)
    {
        List<(ushort Magazine, ushort Gun)> magazines;
        IReadOnlyList<(ushort Slot, byte Index, ushort Weapon)> holsters;

        lock (_cacheLock)
        {
            magazines = _loaded.Select(m => (m.Key, m.Value)).ToList();
            holsters = _slotted.All();
        }

        int sent = 0;

        // Anything its owner has stopped simulating. Without this the newcomer
        // believes somebody is still moving it, so it never takes it over when it
        // walks up to it, and the thing hangs in the air for the whole session.
        foreach (var entity in Entities.Entities)
        {
            if (!entity.CulledForOwner || entity.OwnerSmallId is not { } owner
                || owner == player.SmallId || Players.Get(owner) == null)
            {
                continue;
            }

            SendTo(player.Connection,
                FusionProtocol.BuildCullStatus(owner, player.SmallId, entity.Id, true),
                reliable: true);

            sent++;
        }

        foreach (var (magazine, gun) in magazines)
        {
            if (Entities.Get(magazine) == null || Entities.Get(gun) == null)
            {
                lock (_cacheLock)
                {
                    _loaded.Remove(magazine);
                }

                continue;
            }

            SendTo(player.Connection, ModuleProtocol.WriteModuleToClients(
                ModuleProtocol.MagazineInsertTag, PlayerRegistry.ServerSmallId,
                ModuleProtocol.WriteMagazineInsert(magazine, gun)), reliable: true);

            sent++;
        }

        foreach (var (slot, index, weapon) in holsters)
        {
            if (Entities.Get(weapon) == null)
            {
                lock (_cacheLock)
                {
                    _slotted.Forget(slot, index);
                }

                continue;
            }

            if (!WorldCatchup.ShouldReseat(_grabs.HoldersOf(weapon)))
            {
                continue;
            }

            SendTo(player.Connection, ModuleProtocol.WriteModuleToClients(
                ModuleProtocol.InventorySlotInsertTag, PlayerRegistry.ServerSmallId,
                ModuleProtocol.WriteInventorySlotInsert(slot, weapon, index)), reliable: true);

            Log("INFO", HolsterLog.Resent(Entities.Get(weapon)?.ShortName ?? "an untracked item", weapon,
                    HolsterLog.SlotOwner(slot, smallId => Players.Get(smallId)?.DisplayName), index, player.DisplayName),
                console: false);

            sent++;
        }

        return sent;
    }

    /// <summary>
    /// Follows a magazine into a gun and a weapon into a holster, so the ammo
    /// clock can tell one in use from one abandoned.
    ///
    /// Reading only. These belong to the clients and are passed on untouched.
    /// </summary>
    private void NoteAttachment(ConnectedPlayer sender, long handler, byte[] message)
    {
        var payload = ModuleProtocol.TryReadHandlerPayload(message);

        if (payload == null)
        {
            return;
        }

        var change = ModuleProtocol.ReadAttachment(handler, payload);

        switch (change.Kind)
        {
            case ModuleProtocol.AttachmentKind.Attach:
                Entities.SetAttached(change.Entity, true);

                lock (_cacheLock)
                {
                    if (_loaded.Count < MaxSlotsTracked)
                    {
                        _loaded[change.Entity] = change.Holder;
                    }
                }

                return;

            case ModuleProtocol.AttachmentKind.Detach:
                Entities.SetAttached(change.Entity, false);

                lock (_cacheLock)
                {
                    _loaded.Remove(change.Entity);
                }

                return;

            case ModuleProtocol.AttachmentKind.SlotInsert:
                Entities.SetAttached(change.Entity, true);

                // A hand lets go of what it holsters. The release message can be lost, so the slot insert itself ends the hold.
                _grabs.ReleaseEntity(sender.SmallId, change.Entity);

                lock (_cacheLock)
                {
                    _slotted.Insert(change.Slot, change.SlotIndex, change.Entity);
                }

                LogHolsterChange(sender, change, change.Entity);

                return;

            case ModuleProtocol.AttachmentKind.SlotDrop:
                ushort? dropped;

                lock (_cacheLock)
                {
                    dropped = _slotted.Drop(change.Slot, change.SlotIndex);

                    if (dropped is { } weapon)
                    {
                        Entities.SetAttached(weapon, false);
                    }
                }

                LogHolsterChange(sender, change, dropped);

                return;
        }
    }

    /// <summary>Notes something going into or out of a slot, for the detailed log.</summary>
    private void LogHolsterChange(ConnectedPlayer sender, ModuleProtocol.AttachmentChange change, ushort? item)
    {
        string itemName = item is { } id ? Entities.Get(id)?.ShortName ?? "an untracked item" : "";
        string slotOwner = HolsterLog.SlotOwner(change.Slot, smallId => Players.Get(smallId)?.DisplayName);

        if (HolsterLog.Change(change, item, itemName, slotOwner, sender.DisplayName) is { } line)
        {
            Log("INFO", line, console: false);
        }
    }


    /// <summary>
    /// Clears a constraint, and takes it off our books.
    ///
    /// The delete carries the constraint's entity ID and nothing else. Passing it
    /// on is what makes the constraint disappear on everybody's screen; forgetting
    /// the entity is what stops a constrained pair counting against the cap for
    /// the rest of the session.
    /// </summary>
    private void HandleConstraintDelete(ConnectedPlayer sender, byte[] message)
    {
        // No rank gate here, unlike creating one. A client deletes its own copy
        // only when this message comes back to it, so refusing one leaves the
        // constraint on every screen with nothing able to remove it, including
        // when a constrained prop is despawned. Ownership is the check that
        // matters, and it is below.
        var payload = ModuleProtocol.TryReadHandlerPayload(message);

        if (payload is not { Length: >= 2 })
        {
            return;
        }

        ushort id = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(payload);
        var entity = Entities.Get(id);

        switch (ConstraintDeleteRule.Decide(entity, sender.SmallId, sender.Permission, Config.ExtendedProtection))
        {
            case ConstraintDeleteVerdict.PassOn:
                Log("INFO", $"{sender.DisplayName} cleared constraint {id}, which the server had no record of");
                Relay(sender, message);
                return;

            case ConstraintDeleteVerdict.NotAConstraint:
                Log("WARN", $"{sender.DisplayName} tried to clear {id}, which is not a constraint");
                return;

            case ConstraintDeleteVerdict.NotYours:
                Log("WARN", $"{sender.DisplayName} tried to clear a constraint belonging to " +
                            $"SmallID {entity!.OwnerSmallId}");
                return;
        }

        // Both ends. The message names one, the clients drop both, and an end
        // left on our books would count against the cap for the rest of the
        // session with nothing able to remove it.
        ushort? partner = entity!.Partner;

        lock (_cacheLock)
        {
            _constraints.Remove(id);

            if (partner.HasValue)
            {
                _constraints.Remove(partner.Value);
            }
        }

        if (Entities.Remove(id))
        {
            Log("INFO", partner.HasValue
                ? $"{sender.DisplayName} cleared constraint {id} and {partner.Value}"
                : $"{sender.DisplayName} cleared constraint {id}");
        }

        if (partner.HasValue)
        {
            Entities.Remove(partner.Value);
        }

        Relay(sender, message);
    }

    /// <summary>
    /// Answers a constraint. The two ends become entities of their own, so the host
    /// allocates an id for each, writes them into the message and passes it on. They
    /// are the last two fields, so none of the constraint data before them has to be
    /// understood to do it.
    /// </summary>
    private void HandleConstraintCreate(ConnectedPlayer sender, byte[] message)
    {
        // Said on arrival, before any of the gates below.
        //
        // A client deletes its own constraint before sending this and waits for
        // the server to send it back, so anything refused here looks in game like
        // the constrainer doing nothing at all, for everybody including the person
        // holding it. Without this line there is no way to tell that apart from
        // the message never arriving.
        Log("INFO", $"Constraint request from {sender.DisplayName}");

        if (!sender.Permission.IsAtLeast(Config.Constrainer))
        {
            Log("WARN", $"{sender.DisplayName} tried to constrain but is " +
                        $"{sender.Permission.ToFusionString()}, not " +
                        $"{Config.Constrainer.ToFusionString()}, dropped. " +
                        "Lower the Constrainer rank in the panel to allow it.");
            return;
        }

        var pluginConstraint = Plugins?.Constraint.Raise(new Plugins.ConstraintEvent(
            sender.PlatformId, sender.DisplayName, sender.Permission));

        if (pluginConstraint is { Allowed: false })
        {
            Log("WARN", $"Constraint by {sender.DisplayName} denied by a plugin: " +
                        $"{pluginConstraint.Reason}");
            return;
        }

        byte[]? payload = ModuleProtocol.TryReadHandlerPayload(message);

        if (payload == null)
        {
            Log("WARN", $"Constraint from {sender.DisplayName} did not parse");
            return;
        }

        // Two more entities per constraint, so the world cap has to hold here as
        // well or constraint spam walks straight past it.
        if (Entities.SpawnedCount + 2 > Config.MaxEntities)
        {
            Log("WARN", $"Constraint by {sender.DisplayName} denied: the world is full " +
                        $"({Entities.SpawnedCount}/{Config.MaxEntities}). Clear some props " +
                        "or raise Max entities.");
            return;
        }

        ushort point1 = Entities.AllocateId();
        ushort point2 = Entities.AllocateId();

        byte[]? rewritten = ModuleProtocol.WithPointIds(payload, point1, point2);

        if (rewritten == null)
        {
            Log("WARN", $"Constraint from {sender.DisplayName} was too short to carry its ids");
            return;
        }

        var end1 = Entities.Register(point1, ConstraintBarcode, sender.SmallId, 0, 0, 0);
        var end2 = Entities.Register(point2, ConstraintBarcode, sender.SmallId, 0, 0, 0);

        end1.Synthetic = true;
        end2.Synthetic = true;
        end1.Partner = point2;
        end2.Partner = point1;

        // Kept so somebody joining later is told about it. A host replays every
        // constraint on catch-up; without this everything welded together comes
        // apart for a newcomer while staying joined for everyone else.
        lock (_cacheLock)
        {
            _constraints[point1] = (sender.SmallId, rewritten);
        }

        Broadcast(ModuleProtocol.WriteModuleToClients(
            ModuleProtocol.ConstraintCreateTag, sender.SmallId, rewritten), reliable: true);

        Log("INFO", $"Constraint by {sender.DisplayName}, ends {point1} and {point2}, " +
                    $"sent to {Players.Count} player(s)");
    }

    /// <summary>Stands in for a barcode so the panel and the culls can see these.</summary>
    private const string ConstraintBarcode = "fusion.constraint";

    /// <summary>
    /// The tracker id used for a catch-up spawn. A client allocates its own from
    /// zero upwards, so this is a number none of them will reach.
    /// </summary>
    private const uint CatchupTracker = uint.MaxValue;

    /// <summary>Keeps the "Refused an ownership request" line from repeating every tick.</summary>
    private readonly Dictionary<byte, DateTime> _ownershipRefusalLog = new();

    private void HandleOwnershipRequest(ConnectedPlayer sender, byte[] message)
    {
        var request = TryReadOwnership(message);

        if (request == null)
        {
            // Silence here is a gun that stays where it was for everybody else
            // while the person holding it sees it in their hand: they asked to
            // own it, nothing answered, so they never start simulating it.
            Log("WARN", $"EntityOwnershipRequest from {sender.DisplayName} did not parse");
            return;
        }

        var (requestedOwner, entityId) = request.Value;

        // A LabFusion client only ever asks ownership for itself. A request naming
        // somebody else is spoofed, so it is refused before it reaches a plugin or
        // a tool gate, and nothing is set or announced.
        if (requestedOwner != sender.SmallId)
        {
            DateTime now = Clock();
            var lastRefusal = _ownershipRefusalLog.TryGetValue(sender.SmallId, out var when) ? when : (DateTime?)null;

            if (PoseLogThrottle.ShouldLog(lastRefusal, now))
            {
                _ownershipRefusalLog[sender.SmallId] = now;
                Log("WARN", $"Refused an ownership request from {sender.DisplayName} naming player " +
                            $"{requestedOwner} for entity {entityId}", console: false);
            }

            return;
        }

        if (!MayHold(sender, entityId))
        {
            return;
        }

        var entity = Entities.Get(entityId);

        // Every client that saw the seat has locked the owner to the driver, so
        // granting it to somebody who bumped into it only set the owner bouncing.
        if (entity?.OwnerSmallId is { } driver
            && WorldCatchup.DriverKeeps(sender.SmallId, driver, _seats.SeatOf(driver)?.EntityId,
                _seats.SeatOf(sender.SmallId)?.EntityId, _grabs.HoldersOf(entityId), entityId))
        {
            SendTo(sender.Connection, FusionProtocol.BuildOwnershipResponse(driver, entityId), reliable: true);
            return;
        }

        ulong ownerPlatformId = entity?.OwnerSmallId is { } ownerSmall
            ? Players.Get(ownerSmall)?.PlatformId ?? 0UL
            : 0UL;

        var ownershipVerdict = Plugins?.Ownership.Raise(new Plugins.OwnershipEvent(
            sender.PlatformId, sender.SmallId, sender.DisplayName, sender.Permission,
            entityId, entity?.Barcode ?? "", ownerPlatformId));

        if (ownershipVerdict is { Allowed: false })
        {
            Log("INFO", $"A plugin refused {sender.DisplayName} ownership of entity {entityId}: " +
                        $"{ownershipVerdict.Reason}", console: false);
            return;
        }

        Entities.SetOwner(entityId, requestedOwner);

        // The host's only job here is to confirm; it never claims anything itself.
        AnnounceOwner(entityId, requestedOwner);

        Log("INFO", $"Ownership of entity {entityId} given to player {requestedOwner}, " +
                    $"asked for by {sender.DisplayName}", console: false);
    }

    /// <summary>
    /// Tells everybody who owns an entity now.
    ///
    /// Deciding it here and saying nothing is not enough. When a player leaves,
    /// every client independently drops the owner of everything they held, so a
    /// prop handed to an heir is owned by nobody as far as any of them knows.
    /// Nobody simulates it, and after half a second without a pose their copy
    /// freezes it in place. Adopting an ownerless prop for a newcomer had the
    /// same shape: the server knew, and only the newcomer was told.
    /// </summary>
    private void AnnounceOwner(ushort entityId, byte owner)
    {
        Broadcast(FusionProtocol.BuildOwnershipResponse(owner, entityId), reliable: true);
    }

    /// <summary>
    /// Seat changes go through these so the registry's Occupied flag follows the
    /// seat book, for the entity a rider left and the one they sat in.
    /// </summary>
    private void SeatIngress(byte rider, ushort entityId, byte index, DateTime now)
    {
        lock (_seatLock)
        {
            ushort? before = _seats.SeatOf(rider)?.EntityId;

            _seats.Ingress(rider, entityId, index, now);

            SyncOccupied(before);
            SyncOccupied(entityId);
        }
    }

    private bool SeatEgress(byte rider)
    {
        lock (_seatLock)
        {
            ushort? before = _seats.SeatOf(rider)?.EntityId;

            bool left = _seats.Egress(rider);

            SyncOccupied(before);

            return left;
        }
    }

    private int SeatForgetRider(byte rider)
    {
        lock (_seatLock)
        {
            ushort? before = _seats.SeatOf(rider)?.EntityId;

            int forgotten = _seats.ForgetRider(rider);

            SyncOccupied(before);

            return forgotten;
        }
    }

    private void SyncOccupied(ushort? entityId)
    {
        if (entityId is { } id)
        {
            Entities.SetOccupied(id, _seats.IsOccupied(id));
        }
    }

    /// <summary>
    /// Remembers where a player is standing. Teleporting needs it, and so does
    /// noticing a rider who left a seat without the server hearing.
    /// </summary>
    private void TrackPlayerPose(ConnectedPlayer sender, byte[] message)
    {
        var pose = FusionProtocol.TryReadPlayerPoseUpdate(message);

        if (pose == null)
        {
            return;
        }

        sender.LastPosition = pose.Value.Pose.PelvisPosition;
        sender.HasPosition = true;

        // An egress is sent once and can be missed, which would leave the rider
        // replayed into a vehicle they are nowhere near.
        if (_seats.SeatOf(sender.SmallId) is { } seat
            && Entities.Get(seat.EntityId) is { } vehicle
            && vehicle.PositionKnown
            && SeatBook.IsStale(sender.LastPosition.X, sender.LastPosition.Y, sender.LastPosition.Z,
                vehicle.X, vehicle.Y, vehicle.Z))
        {
            SeatEgress(sender.SmallId);

            Log("INFO", $"{sender.DisplayName} is more than 15 m from entity {seat.EntityId}, " +
                        $"taken out of seat {seat.Index}", console: false);
        }
    }

    /// <summary>
    /// Notes a hit for the panel. Kept off the console because a firefight would
    /// scroll everything else away.
    /// </summary>
    private void RecordHit(ConnectedPlayer sender, byte[] message)
    {
        if (!Config.LogCombat || GateProtocol.TryReadDamage(message) is not { } damage
            || !CombatLog.IsWorthLogging(damage))
        {
            return;
        }

        var (_, _, target) = ServerProtocol.ReadRoute(message);
        string? name = target.HasValue ? Players.Get(target.Value)?.DisplayName : null;

        Log(CombatLog.Level, CombatLog.Describe(sender.DisplayName, name, damage), console: false);
    }

    /// <summary>Keeps the ignored-pose and kept-prop-moved log lines from repeating every tick.</summary>
    private readonly PoseLogThrottle _poseLog = new();

    private void TrackEntityPose(ConnectedPlayer sender, byte[] message)
    {
        var pose = FusionProtocol.TryReadEntityPose(message);

        if (pose == null)
        {
            return;
        }

        ushort vehicleId = pose.Value.EntityId;

        // Only a client that believes it owns a vehicle sends its poses, and
        // Fusion's AtvExtender makes that the driver. So this follows who drives
        // rather than deciding it. This runs before the pose below is judged, so
        // a driver who has just sat down owns the vehicle before their own pose
        // is weighed against the registry.
        if (_seats.SeatOf(sender.SmallId) is { } seat
            && Entities.Get(vehicleId) is { } vehicle
            && WorldCatchup.OwnerFromSeatedPose(sender.SmallId, vehicle.OwnerSmallId, seat.EntityId, vehicleId))
        {
            // MayHold can remove the entity outright, so a refusal must stop
            // here rather than fall into the pose gate below and register the
            // id again as a fresh discovered entity.
            if (!MayHold(sender, vehicleId))
            {
                return;
            }

            Entities.SetOwner(vehicleId, sender.SmallId);
            AnnounceOwner(vehicleId, sender.SmallId);

            Log("INFO", $"Entity {vehicleId} now owned by {sender.DisplayName} (player {sender.SmallId}), " +
                        $"who sits in it in seat {seat.Index}", console: false);
        }

        var known = Entities.Get(vehicleId);

        // A pose from anybody but the entity's current owner is a stale or racing
        // copy: a client only applies a pose from the owner it was told about, so
        // recording this one would silently move the entity for whoever catches up
        // next. A pose for an id nobody spawned is exempt, since that is how scene
        // props are noticed in the first place.
        if (known != null && known.OwnerSmallId != sender.SmallId)
        {
            if (_poseLog.AllowIgnored(vehicleId, sender.SmallId, Clock()))
            {
                string ownerName = known.OwnerSmallId is { } ownerId
                    ? Players.Get(ownerId)?.DisplayName ?? "nobody"
                    : "nobody";

                Log("INFO", $"Ignored a pose for entity {vehicleId} ('{known.ShortName}') from " +
                            $"{sender.DisplayName}, owned by {ownerName}", console: false);
            }

            return;
        }

        // How far the sender stood from it, for the ammo cull's log line.
        float? ownerDistance = sender.HasPosition
            ? new Vec3(
                pose.Value.Position.X - sender.LastPosition.X,
                pose.Value.Position.Y - sender.LastPosition.Y,
                pose.Value.Position.Z - sender.LastPosition.Z).Magnitude
            : null;

        Entities.NotePose(vehicleId, sender.SmallId,
            pose.Value.Position.X, pose.Value.Position.Y, pose.Value.Position.Z,
            pose.Value.Rotation,
            pose.Value.Velocity.X, pose.Value.Velocity.Y, pose.Value.Velocity.Z,
            ownerDistance);

        if (known is { Persistent: true, KeptAt: { } keptAt })
        {
            float moved = new Vec3(
                known.X - keptAt.X, known.Y - keptAt.Y, known.Z - keptAt.Z).Magnitude;

            if (moved > 0.5f && _poseLog.AllowKeptMoved(vehicleId, Clock()))
            {
                Log("INFO", $"Kept '{known.ShortName}' (entity {vehicleId}) is {moved:0.0} m from where " +
                            $"it was kept, pose from {sender.DisplayName}", console: false);
            }
        }
    }

    /// <summary>
    /// Follows what a player holds, reading only, so the grab carries on to the other
    /// clients as usual. Grabbing anything but an entity empties that hand.
    /// </summary>
    private void NoteGrab(ConnectedPlayer sender, byte[] message)
    {
        if (FusionProtocol.TryReadGrab(message) is not { } grab)
        {
            return;
        }

        ushort? letGo = grab.Group == FusionProtocol.GrabGroupEntity && grab.IsGrabbed
            ? _grabs.Grab(sender.SmallId, grab.Hand, grab.EntityId)
            : _grabs.Release(sender.SmallId, grab.Hand);

        if (letGo is { } released)
        {
            PassToHolder(sender, released);
        }
    }

    /// <summary>
    /// Gives an item its owner let go of to whoever still holds it. Fusion only takes an
    /// item back when the last hand on it is the same player's, so nobody asked.
    /// </summary>
    private void PassToHolder(ConnectedPlayer releaser, ushort entityId)
    {
        if (Entities.Get(entityId) is not { } entity
            || WorldCatchup.NextHolder(releaser.SmallId, entity.OwnerSmallId, _grabs.HoldersOf(entityId)) is not { } next
            || Players.Get(next) is not { } holder)
        {
            return;
        }

        // Every client that saw the seat has locked a vehicle to its driver.
        if (WorldCatchup.DriverKeeps(next, entity.OwnerSmallId, _seats.SeatOf(releaser.SmallId)?.EntityId,
                _seats.SeatOf(next)?.EntityId, _grabs.HoldersOf(entityId), entityId))
        {
            return;
        }

        if (!MayHold(holder, entityId))
        {
            return;
        }

        Entities.SetOwner(entityId, next);
        AnnounceOwner(entityId, next);

        Log("INFO", $"Entity {entityId} passed to {holder.DisplayName}, who is still holding it", console: false);
    }

    /// <summary>
    /// Sends a client's request for an entity's state to whoever owns it now, and
    /// tells the asker who that is. Relayed as it was, a request naming an owner who
    /// had left, an old owner, or player 0 was never answered, so the gun in the real
    /// owner's hand floated for the newcomer.
    /// </summary>
    private void HandleEntityDataRequest(ConnectedPlayer sender, byte[] message)
    {
        if (FusionProtocol.TryReadEntityDataRequest(message) is not { } request)
        {
            Relay(sender, message);
            return;
        }

        byte? target = WorldCatchup.DataRequestTarget(
            request.Target,
            Entities.Get(request.EntityId)?.OwnerSmallId,
            sender.SmallId,
            id => Players.Get(id) != null);

        if (target is { } owner && Players.Get(owner) is { } current)
        {
            SendTo(sender.Connection,
                FusionProtocol.BuildOwnershipResponse(owner, request.EntityId), reliable: true);

            SendTo(current.Connection,
                FusionProtocol.BuildEntityDataRequest(sender.SmallId, owner, request.EntityId),
                reliable: true);

            Log("INFO", $"Data request by {sender.DisplayName} for entity {request.EntityId} " +
                        $"redirected from {request.Target?.ToString() ?? "nobody"} to player {owner}", console: false);
        }
        else
        {
            Relay(sender, message);
        }

        ReplayVariables(sender, request.EntityId);
        ReplaySeats(sender, request.EntityId);
    }

    // ---- vehicle seats ----

    /// <summary>
    /// Who sits in which vehicle seat. Fusion sends a seat once, when the rider
    /// sits, so this is the only record a player who arrives later can be given.
    /// </summary>
    private readonly SeatBook _seats = new();

    /// <summary>Around each seat change and its occupancy sync, since Kick can run Depart off the message loop.</summary>
    private readonly object _seatLock = new();

    /// <summary>Who is sitting in a vehicle, by small id, first to sit first.</summary>
    public IReadOnlyList<byte> RidersOf(ushort entityId)
        => _seats.RidersOf(entityId).Select(s => s.Rider).ToList();

    /// <summary>
    /// Keeps the seat book in step with PlayerRepSeat, then passes the message on
    /// as before.
    ///
    /// Only a live seat is kept. One sent ToTarget is Fusion's catch-up reply,
    /// stamped with whoever answered rather than the rider. A seat in an entity
    /// the server does not know is passed on but not kept, since nothing would
    /// ever clear it.
    /// </summary>
    private void HandleSeat(ConnectedPlayer sender, byte[] message)
    {
        if (FusionProtocol.TryReadSeat(message) is { } seat)
        {
            bool known = Entities.Get(seat.SeatId) != null;

            if (!seat.Ingress)
            {
                SeatEgress(sender.SmallId);
            }
            else if (WorldCatchup.KeepSeat(seat.RelayType, seat.Ingress, known))
            {
                SeatIngress(sender.SmallId, seat.SeatId, seat.Index, DateTime.UtcNow);
            }
            else if (WorldCatchup.IsLiveSeat(seat.RelayType))
            {
                // Not kept because the entity is unknown, so the sender must not
                // be left recorded in whatever seat they were in before this one.
                SeatEgress(sender.SmallId);
            }

            Log("INFO", $"{sender.DisplayName} " +
                        $"{(!seat.Ingress ? "got out of" : seat.RelayType == 4 ? "sent a catch-up reply for" : "sat in")} " +
                        $"seat {seat.Index} " +
                        $"of entity {seat.SeatId}, {(known ? "known" : "not known")} to the server, " +
                        $"relay type {seat.RelayType}", console: false);

            if (!WorldCatchup.PassSeatMessage(seat.RelayType, seat.Ingress,
                    _seats.IsRecorded(seat.SeatId, seat.Index)))
            {
                return;
            }
        }

        Relay(sender, message);
    }

    /// <summary>
    /// Sends a client a prop's variables once it has the prop. The replay after
    /// loading lands before a kept prop is spawned, so those values were dropped.
    /// </summary>
    private void ReplayVariables(ConnectedPlayer requester, ushort entityId)
    {
        List<(byte Tag, byte From, byte[] Body)> variables;

        lock (_cacheLock)
        {
            variables = _rpcVariables.ForEntity(entityId);
        }

        foreach (var (tag, from, body) in variables)
        {
            SendRpcVariable(requester, tag, from, body);
        }

        if (variables.Count > 0)
        {
            Log("INFO", $"Replayed {variables.Count} variable(s) on entity {entityId} to {requester.DisplayName}",
                console: false);
        }
    }

    /// <summary>
    /// Tells a client who is sitting in a vehicle it has just asked about.
    ///
    /// Fusion's own answer seats whoever answered instead of the rider, so a
    /// player who arrived after the riders saw them on the hood. Each seat goes
    /// out as its rider would have sent it.
    /// </summary>
    private void ReplaySeats(ConnectedPlayer requester, ushort entityId)
    {
        var seats = WorldCatchup.SeatsToReplay(_seats.RidersOf(entityId), entityId, requester.SmallId,
            id => Players.Get(id) != null);

        foreach (var seat in seats)
        {
            SendTo(requester.Connection, FusionProtocol.BuildSeat(seat.Rider, entityId, seat.Index, true),
                reliable: true);
        }

        if (seats.Count > 0)
        {
            Log("INFO", $"Replayed {seats.Count} seat(s) in entity {entityId} to {requester.DisplayName}",
                console: false);
        }
    }

    // ---- relaying ----

    /// <summary>Server-addressed tags already reported, so each is said once.</summary>
    private readonly HashSet<byte> _unhandledTags = new();

    /// <summary>
    /// Messages only a host may send. A client sending one is ignored.
    ///
    /// Fusion refuses these on the receiving side: a handler marked ClientsOnly
    /// throws when it sees a message the receiver handled as the server, and the
    /// throw is swallowed. So on a real lobby a client forging one of these
    /// achieves nothing.
    ///
    /// A relay forwards on the route byte alone, which handed every one of them
    /// straight through. That is not a small hole. SpawnResponse alone puts any
    /// barcode in front of every player without passing the blocklist, the tool
    /// gate, the rank check, the rate limiter, the entity cap or any plugin;
    /// Disconnect names a player and their game leaves; SceneLoad moves the whole
    /// server; EntityOwnershipResponse takes an object out of somebody's hands.
    /// Every gate this server has was reachable around.
    /// </summary>
    private static readonly HashSet<byte> HostOnlyTags = new()
    {
        2,    // ConnectionResponse, invents players
        3,    // Disconnect, removes them
        12,   // SceneLoad, moves everybody
        14,   // EntityUnqueueResponse, corrupts another client's queue
        16,   // EntityOwnershipResponse, takes an object from its holder
        21,   // SpawnResponse, spawns anything past every gate
        23,   // DespawnResponse, removes anything
        45,   // ServerSettings, rewrites the rules on every client
        60,   // PlayerMetadataResponse, writes anybody's metadata
        69,   // PlayerRepTeleport, moves a player
        201,  // DynamicsAssignment
        202,  // GamemodeMetadataSet
        203,  // GamemodeMetadataRemove
    };

    private void Relay(ConnectedPlayer sender, byte[] message)
    {
        if (HostOnlyTags.Contains(message[0]))
        {
            if (_forgedTags.Add(message[0]))
            {
                Log("WARN", $"{sender.DisplayName} sent tag {message[0]} " +
                            $"({NameOfTag(message[0])}), which only a server may send. " +
                            "Ignored. A stock client does not do this.");
            }

            return;
        }

        var (relayType, channel, target) = ServerProtocol.ReadRoute(message);

        bool reliable = channel != 1;
        var stamped = ServerProtocol.StampSender(message, sender.SmallId);

        switch (relayType)
        {
            case 0: // None, meant for the server alone
            case 1: // ToServer
                // Nothing forwards a message addressed to the server, so anything
                // reaching here is a request nobody implemented, and it fails in
                // silence. Teleporting was lost this way for months. Once per tag,
                // because a client that keeps asking would fill the log.
                if (_unhandledTags.Add(message[0]))
                {
                    Log("WARN", $"Message tag {message[0]} ({NameOfTag(message[0])}) is " +
                                "addressed to the server and nothing here answers it, so it " +
                                "was dropped");
                }

                return;

            case 2: // ToClients, everyone, sender included
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

            case 5: // ToTargets, the listed players only
                foreach (byte listedId in ServerProtocol.ReadTargets(message))
                {
                    if (Players.Get(listedId) is { } listed)
                    {
                        SendTo(listed.Connection, stamped, reliable);
                    }
                }

                return;

            default:
                Broadcast(stamped, reliable, except: sender.SmallId);
                return;
        }
    }

    /// <summary>
    /// What a native tag is called, for the one log line that says something was
    /// dropped. Every message a client addresses to the server is named here, so
    /// that line points at the gap instead of leaving somebody to find a number
    /// in the decompiled game.
    /// </summary>
    private static string NameOfTag(byte tag) => tag switch
    {
        1 => "ConnectionRequest",
        3 => "Disconnect",
        13 => "EntityUnqueueRequest",
        15 => "EntityOwnershipRequest",
        20 => "SpawnRequest",
        22 => "DespawnRequest",
        59 => "PlayerMetadataRequest",
        2 => "ConnectionResponse",
        12 => "SceneLoad",
        14 => "EntityUnqueueResponse",
        16 => "EntityOwnershipResponse",
        18 => "NetworkPropCreate",
        21 => "SpawnResponse",
        23 => "DespawnResponse",
        45 => "ServerSettings",
        60 => "PlayerMetadataResponse",
        62 => "LevelRequest, which only the panel may do here",
        69 => "PlayerRepTeleport",
        201 => "DynamicsAssignment",
        202 => "GamemodeMetadataSet",
        203 => "GamemodeMetadataRemove",
        209 => "RPCEvent addressed to the server, which needs a game to run it",
        68 => "PermissionCommandRequest",
        _ => "unknown",
    };

    public void Broadcast(byte[] message, bool reliable, byte? except = null)
    {
        foreach (var player in Players.Players)
        {
            if (except.HasValue && player.SmallId == except.Value)
            {
                continue;
            }

            if (SendTo(player.Connection, message, reliable))
            {
                player.BytesOut += message.Length;
            }
        }
    }

    public bool SendTo(HSteamNetConnection connection, byte[] message, bool reliable)
    {
        bool sent = _transport.Send(connection, message, reliable);

        if (sent)
        {
            PacketsOut++;
            BytesOut += message.Length;
        }

        return sent;
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

        var connection = player.Connection;

        // Them first, so they are told why before the socket goes.
        SendTo(connection, ServerProtocol.WriteDisconnect(player.PlatformId, reason), reliable: true);

        // Then everybody else, here rather than from the disconnect callback,
        // because closing the connection ourselves does not raise one.
        // Kicked, not why. The reason belongs to them and the log.
        if (Players.Remove(connection) is { } gone)
        {
            Depart(gone, reason, announce: "Removed from the server");
        }

        // Their entry is gone, so nothing downstream can recognise them any more.
        // ConnectionRequest is the one message with no sender check, and without
        // this a kicked client could hand in a fresh request inside the window
        // before the socket closes and be let straight back in.
        lock (_closingLock)
        {
            _closing.Add(connection.m_HSteamNetConnection);
        }

        Task.Delay(250).ContinueWith(_ =>
        {
            // Released whatever happens. Steam reuses connection handles, so one
            // left behind here silently refuses whoever lands on it next.
            try
            {
                _transport.Close(connection, reason);
            }
            finally
            {
                lock (_closingLock)
                {
                    _closing.Remove(connection.m_HSteamNetConnection);
                }
            }
        });
    }

    /// <summary>Periodic housekeeping: republish settings and drop dead entities.</summary>
    public void Tick()
    {
        // A client builds its rules purely from the ServerSettings message. If it ever
        // misses one, or its own scene-load hook overwrites LobbyInfo with local
        // preferences, it falls back to LobbyInfo.Empty, where mortality and knockout
        // are both off. In game that looks like being unkillable with nothing happening
        // on death, so it is worth a small reliable message to keep everyone converged.
        if (Players.Count > 0)
        {
            PushSettings();
        }

        foreach (var (player, kind, count) in _refusals.Flush(Clock()))
        {
            string name = Players.Get(player)?.DisplayName ?? $"player {player}";
            Log("WARN", $"{name}: {count} more {kind} refusal(s) held back from the log");
        }

        if (!Config.CullOrphanedEntities)
        {
            return;
        }

        var culled = Entities.CullStaleDetailed(
            TimeSpan.FromSeconds(Config.OrphanTimeoutSeconds),
            TimeSpan.FromSeconds(Config.InheritedTimeoutSeconds),
            TimeSpan.FromSeconds(Math.Max(0, Config.IdleTimeoutSeconds)),
            TimeSpan.FromSeconds(Math.Max(0, Config.AmmoTimeoutSeconds)),
            EntitiesInUse());

        var removed = culled.Select(e => e.Id).ToList();

        if (removed.Count > 0)
        {
            // Forgetting them here is not enough: they stay in every client's world,
            // and their ids go back in the pool. Since ids are only a ushort, a busy
            // server works its way round the range in about a week and would then
            // hand a recycled id to a prop the clients still have, two different
            // objects under one id. So the cull has to be broadcast, not just booked.
            DespawnOnClients(removed);

            Log("INFO", $"Culled {removed.Count} abandoned entities " +
                        $"({Entities.Count} left in world)");

            var now = Clock();

            foreach (var entity in culled)
            {
                bool ownerOnline = entity.OwnerSmallId is { } owner && Players.Get(owner) != null;

                if (AmmoDiagnostics.DescribeCull(entity, ownerOnline, now) is { } line)
                {
                    Log("INFO", line, console: false);
                }
            }
        }

        // Warn before the allocator laps rather than after.
        if (Entities.NextId > ushort.MaxValue - 4096)
        {
            Log("WARN", $"Entity ids near the top of the range ({Entities.NextId}/{ushort.MaxValue}); " +
                        "the allocator will wrap and reuse freed ids");
        }
    }

    // ---- small readers ----

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
