using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using FusionDedicated.Server;

namespace FusionDedicated.Web;

/// <summary>
/// Tiny embedded control panel. Uses HttpListener so there is no web framework to
/// install; the page itself polls a JSON endpoint.
/// </summary>
public sealed class Dashboard
{
    private readonly FusionServer _server;
    private readonly ServerConfig _config;
    private readonly LobbyPublisher _lobby;
    private readonly HttpListener _listener = new();

    /// <summary>Accounts allowed into the panel, beyond the one in its settings.</summary>
    public PanelUsers Users { get; } = new(Path.Combine(
        AppContext.BaseDirectory, "panel-users.json"));

    /// <summary>Who is making the request being handled, for the audit trail.</summary>
    private string _acting = "panel";

    private PanelRole _actingRole = PanelRole.Owner;

    /// <summary>Pages offered by plugins, when a host is running.</summary>
    public Plugins.PluginPanel? PluginPanel { get; set; }

    private string ActorFor(HttpListenerContext context)
    {
        var parsed = DashboardAuth.TryParseBasic(context.Request.Headers["Authorization"]);

        return parsed?.User is { Length: > 0 } name ? name : _config.DashboardUser;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Dashboard(FusionServer server, ServerConfig config, LobbyPublisher lobby)
    {
        _server = server;
        _config = config;
        _lobby = lobby;
    }

    /// <summary>Prefix HttpListener binds to.</summary>
    private string Prefix => $"http://{_config.DashboardHost}:{_config.DashboardPort}/";

    /// <summary>Address a human can actually type.</summary>
    public string Url => DashboardAddress.Format(
        _config.DashboardHost, _config.DashboardPublicHost, _config.DashboardPort);

    public bool IsListening => _listener.IsListening;

    public void Start()
    {
        var refusal = DashboardAuth.BindRefusalReason(
            _config.DashboardHost, _config.DashboardPassword);

        if (refusal != null)
        {
            _server.Log("ERROR", $"Control panel not started: {refusal}");
            return;
        }

        Users.Load();

        if (Users.Count > 0)
        {
            _server.Log("INFO", $"Panel accounts: {Users.Count} besides {_config.DashboardUser}");
        }

        _listener.Prefixes.Add(Prefix);
        _listener.Start();

        _ = Task.Run(LoopAsync);
    }

    public void Stop()
    {
        try { _listener.Stop(); } catch { }
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            try
            {
                Handle(context);
            }
            catch (Exception ex)
            {
                _server.Log("ERROR", $"Panel: {ex.Message}");
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var role = PanelSignIn.Resolve(
            context.Request.Headers["Authorization"],
            _config.DashboardUser,
            _config.DashboardPassword,
            Users);

        if (role is null)
        {
            context.Response.StatusCode = 401;
            context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"Fusion Dedicated\"");
            context.Response.Close();
            return;
        }

        string path = context.Request.Url?.AbsolutePath ?? "/";
        var query = context.Request.QueryString;

        // Checked here rather than in each handler, so a new endpoint that
        // nobody placed in the table is refused instead of quietly open.
        if (!PanelPermissions.Allows(role.Value, path))
        {
            context.Response.StatusCode = 403;
            Write(context, "text/plain", "Your account is not allowed to do that.");
            return;
        }

        _acting = ActorFor(context);
        _actingRole = role.Value;

        switch (path)
        {
            case "/":
            case "/index.html":
                ServePage(context);
                return;

            case "/api/state":
                // A banker is given the shell and nothing to put in it. They need
                // this to draw the panel at all, and hiding the rest in the page
                // would only be hiding it: the answer would still have been sent.
                ServeJson(context, _actingRole == PanelRole.Banker
                    ? BuildBankerState()
                    : BuildState());
                return;

            case "/api/kick":
                HandleKick(context, query);
                return;

            case "/api/ban":
                HandleBan(context, query);
                return;

            case "/api/bannote":
                HandleBanNote(context, query);
                return;

            case "/api/unban":
                HandleUnban(context, query);
                return;

            case "/api/permission":
                HandlePermission(context, query);
                return;

            case "/api/settings":
                HandleSettings(context, query);
                return;

            case "/api/level":
                HandleLevel(context, query);
                return;

            case "/api/levels":
                HandleLevels(context, query);
                return;

            case "/api/clear":
                ServeJson(context, new
                {
                    ok = true,
                    removed = _server.ClearAllEntities(
                        query["includeDiscovered"] == "1" || _config.ClearDiscoveredEntities),
                });
                return;

            case "/api/gather":
                HandleGather(context, query);
                return;

            case "/api/despawn":
                HandleDespawn(context, query);
                return;

            case "/api/persist":
                HandlePersist(context, query);
                return;

            case "/api/mute":
                HandleMute(context, query, muted: true);
                return;

            case "/api/unmute":
                HandleMute(context, query, muted: false);
                return;

            case "/api/audit":
                ServeJson(context, _server.AuditTrail?.Recent(200) ?? Array.Empty<object>().Cast<object>());
                return;

            case "/api/purge":
                HandlePurge(context, query);
                return;

            case "/api/restart":
                HandleRestart(context, query);
                return;

            case "/api/modules":
            {
                if (query["enabled"] is { Length: > 0 } wanted)
                {
                    _server.ModuleInspector.Enabled = wanted == "1" || wanted == "true";
                    _config.ModuleInspection = _server.ModuleInspector.Enabled;
                    _config.Save(Program.ConfigPath);
                }

                ServeJson(context, new
                {
                    ok = true,
                    enabled = _server.ModuleInspector.Enabled,
                    seen = _server.ModuleInspector.Recent,
                });
                return;
            }

            case "/api/plugins":
                ServeJson(context, new
                {
                    ok = true,
                    plugins = VisiblePlugins(),
                });
                return;

            case "/api/plugins/page":
            {
                var page = VisiblePage(query["plugin"] ?? "");

                if (page == null)
                {
                    context.Response.StatusCode = 403;
                    ServeJson(context, new { ok = false, error = "not allowed" });
                    return;
                }

                ServeJson(context, page);
                return;
            }

            case "/api/plugins/action":
            {
                string plugin = query["plugin"] ?? "";
                string action = query["action"] ?? "";

                // The buttons somebody can see are the buttons they may press.
                // Without this a banker, who is shown one block of one page, could
                // still ask for any action any plugin has registered.
                if (!MayInvoke(plugin, action))
                {
                    context.Response.StatusCode = 403;
                    ServeJson(context, new { ok = false, error = "not allowed" });
                    return;
                }

                var values = query.AllKeys
                    .Where(k => k != null && k != "plugin" && k != "action")
                    .ToDictionary(k => k!, k => query[k] ?? "");

                var result = PluginPanel?.Invoke(plugin, action, values)
                    ?? Plugins.PanelActionResult.Failed("plugins are off");

                ServeJson(context, new { ok = result.Handled, error = result.Error });
                return;
            }

            case "/api/accounts":
                HandleAccounts(context, query);
                return;

            case "/api/history":
                ServeJson(context, BuildHistory(query));
                return;

            default:
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
        }
    }

    /// <summary>
    /// A plugin page with the blocks this account may not see taken out, or null
    /// when there is nothing left for them.
    ///
    /// Filtered here rather than when the page is built, so a plugin describes
    /// its page once and the panel decides who sees which part of it.
    /// </summary>
    private Plugins.PluginPage? VisiblePage(string plugin)
    {
        var page = PluginPanel?.Build(plugin);

        if (page == null)
        {
            return null;
        }

        page.Sections = page.Sections
            .Where(section => PanelPermissions.CanSee(_actingRole, section.Required))
            .ToList();

        if (page.Sections.Count == 0)
        {
            return null;
        }

        // A banker is allowed a page by the blocks marked for them, not by the
        // page's own floor, which every page sets to moderator.
        return _actingRole == PanelRole.Banker || PanelPermissions.CanSee(_actingRole, page.Required)
            ? page
            : null;
    }

    /// <summary>Plugins with at least one block this account may see.</summary>
    private IReadOnlyList<string> VisiblePlugins()
    {
        var names = PluginPanel?.Pages ?? (IReadOnlyList<string>)Array.Empty<string>();

        return names.Where(name => VisiblePage(name) != null).ToList();
    }

    /// <summary>
    /// Whether an action is one of the buttons this account is shown. Row buttons
    /// count as well as a block's own, since a table's rows carry their own.
    /// </summary>
    private bool MayInvoke(string plugin, string action)
    {
        if (VisiblePage(plugin) is not { } page)
        {
            return false;
        }

        foreach (var section in page.Sections)
        {
            if (section.Buttons.Any(b => b.Action == action)
                || section.Rows.Any(r => r.Actions.Any(b => b.Action == action)))
            {
                return true;
            }
        }

        return false;
    }

    private void ServePage(HttpListenerContext context)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "Web", "index.html");

        if (!File.Exists(file))
        {
            file = Path.Combine(AppContext.BaseDirectory, "index.html");
        }

        if (!File.Exists(file))
        {
            context.Response.StatusCode = 500;
            Write(context, "text/plain", "index.html was not found next to the executable");
            return;
        }

        string page = File.ReadAllText(file);
        string tag = PageCache.ETagFor(page);

        context.Response.Headers["ETag"] = tag;

        // no-cache asks the browser to check rather than trust an age, so a
        // release is picked up without anyone being told to hard refresh, and an
        // unchanged panel still costs only the check.
        context.Response.Headers["Cache-Control"] = "no-cache";

        if (PageCache.IsFresh(context.Request.Headers["If-None-Match"], tag))
        {
            context.Response.StatusCode = 304;
            context.Response.Close();
            return;
        }

        Write(context, "text/html; charset=utf-8", page);
    }

    /// <summary>
    /// Enough for the panel to draw its frame, and no more. Every count is zero
    /// and every list is empty, because a banker is not being shown the server.
    /// </summary>
    private object BuildBankerState() => new
    {
        server = new
        {
            name = _config.ServerName,
            description = "",
            level = "",
            levelBarcode = "",
            version = _config.Version,
            code = "",
            privacy = "",
            maxPlayers = 0,
            lobbyId = 0UL,
            published = _lobby.IsPublished,
            uptimeSeconds = 0,
            levelModId = -1,
            levelModFileId = (int?)null,
        },
        players = Array.Empty<object>(),
        bans = Array.Empty<object>(),
        log = Array.Empty<object>(),
        entities = new { total = 0, orphaned = 0 },
        traffic = new { packetsIn = 0L, packetsOut = 0L, bytesIn = 0L, bytesOut = 0L },
        limits = new { maxEntities = 0 },
        you = new { role = _actingRole.ToString().ToLowerInvariant() },
    };

    private object BuildState()
    {
        var players = _server.Players.Players;
        var uptime = DateTime.UtcNow - _server.StartedAt;

        return new
        {
            server = new
            {
                name = _config.ServerName,
                description = _config.Description,
                level = _config.LevelTitle,
                levelBarcode = _config.LevelBarcode,
                version = _config.Version,
                code = _config.ServerCode,
                privacy = _config.Privacy,
                maxPlayers = _config.MaxPlayers,
                lobbyId = _lobby.LobbyId,
                published = _lobby.IsPublished,
                uptimeSeconds = (int)uptime.TotalSeconds,
                levelModId = _config.LevelModId,
                levelModFileId = _config.LevelModFileId,
            },
            levels = _config.Levels.Select(l => new
            {
                title = l.Title,
                barcode = l.Barcode,
                modId = l.ModId,
                modFileId = l.ModFileId,
                active = l.Barcode == _config.LevelBarcode,
            }).ToArray(),
            modCatalog = _config.ModCatalog
                .OrderByDescending(m => m.LearnedAt)
                .Take(200)
                .Select(m => new
                {
                    barcode = m.Barcode,
                    modId = m.ModId,
                    modFileId = m.ModFileId,
                    learnedAt = m.LearnedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                }).ToArray(),
            guard = new
            {
                enabled = _config.AntiSpamEnabled,
                burstLimit = _config.SpawnBurstLimit,
                windowSeconds = _config.SpawnWindowSeconds,
                maxPerPlayer = _config.MaxEntitiesPerPlayer,
                strikes = _config.SpamStrikesBeforeKick,
                exemptLevel = (int)_config.AntiSpamExemptLevel,
            },
            resources = BuildResources(),
            gameplay = new
            {
                nameTags = _config.NameTags,
                voiceChat = _config.VoiceChat,
                playerConstraining = _config.PlayerConstraining,
                mortality = _config.Mortality,
                friendlyFire = _config.FriendlyFire,
                knockout = _config.Knockout,
                knockoutLength = _config.KnockoutLength,
                maxAvatarHeight = _config.MaxAvatarHeight,
                slowMoMode = _config.SlowMoMode,
            },
            permissions = new
            {
                devTools = (int)_config.DevTools,
                constrainer = (int)_config.Constrainer,
                nimbus = (int)_config.Nimbus,
                spawning = (int)_config.Spawning,
                customAvatars = (int)_config.CustomAvatars,
                kicking = (int)_config.Kicking,
                banning = (int)_config.Banning,
                teleportation = (int)_config.Teleportation,
            },
            limits = new
            {
                maxEntities = _config.MaxEntities,
                cullOrphans = _config.CullOrphanedEntities,
                idleTimeoutSeconds = _config.IdleTimeoutSeconds,
                ammoTimeoutSeconds = _config.AmmoTimeoutSeconds,
                orphanTimeoutSeconds = _config.OrphanTimeoutSeconds,
            },
            traffic = new
            {
                packetsIn = _server.PacketsIn,
                packetsOut = _server.PacketsOut,
                bytesIn = _server.BytesIn,
                bytesOut = _server.BytesOut,
            },
            players = players.Select(p => new
            {
                smallId = p.SmallId,
                platformId = p.PlatformId.ToString(),
                name = p.DisplayName,
                username = p.Username,
                avatar = p.AvatarBarcode,
                version = $"{p.Version.Major}.{p.Version.Minor}",
                permission = (int)p.Permission,
                onlineSeconds = (int)(DateTime.UtcNow - p.JoinedAt).TotalSeconds,
                bytesIn = p.BytesIn,
                bytesOut = p.BytesOut,
                entities = _server.Entities.Entities.Count(e => e.OwnerSmallId == p.SmallId),
            }).ToArray(),

            // Saved ranks, including people who are not connected right now.
            // ranks.json is authoritative when it exists, so the panel reads what is
            // actually in force rather than the config list it superseded.
            roster = (_server.Ranks is { } ranks
                    ? ranks.Entries.Select(e => (e.Key, e.Value.Name, e.Value.Rank))
                    : _config.Permissions.Select(e => (e.PlatformId, e.Username, e.Level)))
                .OrderByDescending(e => e.Item3)
                .Select(e => new
                {
                    platformId = e.Item1.ToString(),
                    username = e.Item2,
                    level = (int)e.Item3,
                    online = players.Any(p => p.PlatformId == e.Item1),
                }).ToArray(),

            muted = _server.Mutes.Muted.Select(id => id.ToString()).ToArray(),

            you = new
            {
                name = _acting,
                role = _actingRole.ToString().ToLowerInvariant(),
            },

            whitelist = new
            {
                enabled = _server.Members?.Enabled ?? false,
                count = _server.Members?.Entries.Count ?? 0,
            },

            world = new
            {
                total = _server.Entities.Count,
                discovered = _server.Entities.DiscoveredCount,
            },

            // bans.json when it is in use, falling back to the config list so a
            // server upgraded mid-life still shows its old bans.
            bans = (_server.BanList is { } list
                    ? list.Entries.Select(e => (e.Key, e.Value.Name, e.Value.Reason, e.Value.BannedAt, e.Value.Note))
                    : _config.Bans.Select(b => (b.PlatformId, b.Username, b.Reason, b.BannedAt, b.Note)))
                .OrderByDescending(b => b.Item4)
                .Select(b => new
                {
                    platformId = b.Item1.ToString(),
                    username = b.Item2,
                    reason = b.Item3,
                    note = b.Item5,
                    bannedAt = b.Item4.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    expiresAt = _server.BanList?.Find(b.Item1)?.ExpiresAt?
                        .ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                }).ToArray(),

            entities = new
            {
                total = _server.Entities.Count,
                orphaned = _server.Entities.OrphanCount,
                discovered = _server.Entities.DiscoveredCount,
                recent = _server.Entities.Entities
                    .OrderByDescending(e => e.LastUpdate)
                    .Take(60)
                    .Select(e => new
                    {
                        id = e.Id,
                        name = e.ShortName,
                        barcode = e.Barcode,
                        owner = e.OwnerSmallId,
                        orphaned = e.IsOrphaned,
                        persistent = e.Persistent,
                        x = MathF.Round(e.X, 1),
                        y = MathF.Round(e.Y, 1),
                        z = MathF.Round(e.Z, 1),
                    }).ToArray(),
            },

            log = _server.RecentLog(200).Select(l => new
            {
                at = l.At.ToLocalTime().ToString("HH:mm:ss"),
                level = l.Level,
                message = l.Message,
            }).Reverse().ToArray(),
        };
    }

    private object BuildResources()
    {
        var latest = _server.Resources.Latest;
        var host = _server.Resources.ReadHost();

        return new
        {
            cpuPercent = latest?.CpuPercent ?? 0,
            workingSetBytes = latest?.WorkingSetBytes ?? 0,
            managedBytes = latest?.ManagedBytes ?? 0,
            threads = latest?.Threads ?? 0,
            packetsInPerSecond = Math.Round(_server.Resources.PacketsInPerSecond, 1),
            packetsOutPerSecond = Math.Round(_server.Resources.PacketsOutPerSecond, 1),
            bytesInPerSecond = Math.Round(_server.Resources.BytesInPerSecond, 0),
            bytesOutPerSecond = Math.Round(_server.Resources.BytesOutPerSecond, 0),
            host = new
            {
                loadAverage = host.LoadAverage,
                memoryTotalBytes = host.MemoryTotalBytes,
                memoryAvailableBytes = host.MemoryAvailableBytes,
                processorCount = host.ProcessorCount,
                platform = host.Platform,
                framework = host.Framework,
            },
        };
    }

    /// <summary>
    /// Rolling samples for the panel's graphs. Kept on its own endpoint so the
    /// once-a-second state poll does not carry hundreds of points every time.
    /// </summary>
    private static readonly Dictionary<string, TimeSpan> Ranges = new()
    {
        ["10m"] = TimeSpan.FromMinutes(10),
        ["1h"] = TimeSpan.FromHours(1),
        ["6h"] = TimeSpan.FromHours(6),
        ["1d"] = TimeSpan.FromDays(1),
        ["1w"] = TimeSpan.FromDays(7),
        ["1M"] = TimeSpan.FromDays(30),
    };

    private object BuildHistory(NameValueCollection query)
    {
        string key = query["range"] ?? "1h";
        var range = Ranges.GetValueOrDefault(key, TimeSpan.FromHours(1));

        // Roughly one point per two pixels of chart; more is wasted bytes.
        int maxPoints = int.TryParse(query["points"], out var p) ? Math.Clamp(p, 20, 800) : 320;

        var samples = _server.Resources.Query(range, maxPoints);

        return new
        {
            range = key,
            points = samples.Count,
            // Counters are cumulative, so the panel needs the spacing to turn them
            // into rates, and the spacing changes with the range.
            stepSeconds = samples.Count > 1
                ? Math.Max(1, (int)(samples[^1].At - samples[0].At).TotalSeconds / (samples.Count - 1))
                : 5,
            samples = samples.Select(sm => new
            {
                at = sm.At.ToLocalTime().ToString(range >= TimeSpan.FromDays(1) ? "MM-dd HH:mm" : "HH:mm:ss"),
                cpu = sm.CpuPercent,
                memory = sm.WorkingSetBytes,
                threads = sm.Threads,
                players = sm.Players,
                entities = sm.Entities,
                packetsIn = sm.PacketsIn,
                packetsOut = sm.PacketsOut,
                bytesIn = sm.BytesIn,
                bytesOut = sm.BytesOut,
            }).ToArray(),
        };
    }

    // ---- moderation ----

    private void HandleKick(HttpListenerContext context, NameValueCollection query)
    {
        if (byte.TryParse(query["id"], out var smallId))
        {
            string reason = query["reason"] is { Length: > 0 } r ? r : "Kicked by server operator";
            _server.Kick(smallId, reason);
        }

        ServeJson(context, new { ok = true });
    }

    /// <summary>
    /// Accepts either a connected player's small ID or a raw SteamID, so someone who
    /// already left can still be banned from the bans view.
    /// </summary>
    /// <summary>
    /// Writes the admin note on a ban. Separate from banning so it can be added
    /// later, which is when most of the story is actually known.
    /// </summary>
    private void HandleBanNote(HttpListenerContext context, NameValueCollection query)
    {
        if (!ulong.TryParse(query["platformId"], out var platformId))
        {
            ServeJson(context, new { ok = false, error = "no such ban" });
            return;
        }

        string note = query["note"] ?? "";
        bool written = _server.BanList?.SetNote(platformId, note) ?? false;

        if (!written)
        {
            foreach (var entry in _config.Bans.Where(b => b.PlatformId == platformId))
            {
                entry.Note = note;
                written = true;
            }
        }

        if (!written)
        {
            ServeJson(context, new { ok = false, error = "no such ban" });
            return;
        }

        _server.BanList?.Save();
        _config.Save(Program.ConfigPath);

        _server.Log("INFO", $"Note written on the ban for {platformId} by {_acting}");
        ServeJson(context, new { ok = true });
    }

    private void HandleBan(HttpListenerContext context, NameValueCollection query)
    {
        string reason = query["reason"] is { Length: > 0 } r ? r : "Banned from Server";
        string note = query["note"] ?? "";

        if (byte.TryParse(query["id"], out var smallId) && _server.Players.Get(smallId) is { } player)
        {
            _server.Ban(player.PlatformId, player.Username, reason,
                null, Server.Audit.AuditChannel.Panel, _acting, note);
        }
        else if (ulong.TryParse(query["platformId"], out var platformId))
        {
            _server.Ban(platformId, query["username"] ?? "", reason,
                null, Server.Audit.AuditChannel.Panel, _acting, note);
        }
        else
        {
            ServeJson(context, new { ok = false, error = "no such player" });
            return;
        }

        _config.Save(Program.ConfigPath);
        ServeJson(context, new { ok = true });
    }

    private void HandleMute(HttpListenerContext context, NameValueCollection query, bool muted)
    {
        if (!ulong.TryParse(query["platformId"], out ulong platformId))
        {
            context.Response.StatusCode = 400;
            ServeJson(context, new { ok = false, error = "platformId is required" });
            return;
        }

        string name = _server.Players.GetByPlatformId(platformId)?.DisplayName ?? "";

        if (muted)
        {
            _server.MutePlayer(platformId, name);
        }
        else
        {
            _server.UnmutePlayer(platformId, name);
        }

        ServeJson(context, new { ok = true });
    }

    private void HandleUnban(HttpListenerContext context, NameValueCollection query)
    {
        if (!ulong.TryParse(query["platformId"], out var platformId) || !_server.Unban(platformId))
        {
            ServeJson(context, new { ok = false, error = "not banned" });
            return;
        }

        _config.Save(Program.ConfigPath);
        ServeJson(context, new { ok = true });
    }

    private void HandlePermission(HttpListenerContext context, NameValueCollection query)
    {
        if (!int.TryParse(query["level"], out var rawLevel))
        {
            ServeJson(context, new { ok = false, error = "missing level" });
            return;
        }

        var level = PermissionLevels.Clamp(rawLevel);

        if (byte.TryParse(query["id"], out var smallId) && _server.Players.Get(smallId) is { } player)
        {
            _server.SetPermission(player.PlatformId, player.Username, level);
        }
        else if (ulong.TryParse(query["platformId"], out var platformId))
        {
            _server.SetPermission(platformId, query["username"] ?? "", level);
        }
        else
        {
            ServeJson(context, new { ok = false, error = "no such player" });
            return;
        }

        _config.Save(Program.ConfigPath);
        ServeJson(context, new { ok = true });
    }

    // ---- world & lifecycle ----

    private void HandleLevel(HttpListenerContext context, NameValueCollection query)
    {
        string barcode = query["barcode"] ?? "";

        if (string.IsNullOrWhiteSpace(barcode))
        {
            ServeJson(context, new { ok = false, error = "missing barcode" });
            return;
        }

        int modId = int.TryParse(query["modId"], out var m) ? m : -1;
        int? modFileId = int.TryParse(query["modFileId"], out var f) && f > 0 ? f : null;

        _server.SetLevel(barcode, query["title"] ?? barcode, modId, modFileId);
        _config.Save(Program.ConfigPath);

        ServeJson(context, new { ok = true });
    }

    /// <summary>Adds or removes an entry in the saved map list.</summary>
    private void HandleLevels(HttpListenerContext context, NameValueCollection query)
    {
        string barcode = query["barcode"] ?? "";

        if (string.IsNullOrWhiteSpace(barcode))
        {
            ServeJson(context, new { ok = false, error = "missing barcode" });
            return;
        }

        if (query["remove"] == "true")
        {
            _config.Levels.RemoveAll(l => l.Barcode == barcode);
        }
        else
        {
            var existing = _config.Levels.FirstOrDefault(l => l.Barcode == barcode);
            var entry = existing ?? new LevelEntry { Barcode = barcode };

            if (query["title"] is { Length: > 0 } title)
            {
                entry.Title = title;
            }

            entry.ModId = int.TryParse(query["modId"], out var m) ? m : -1;
            entry.ModFileId = int.TryParse(query["modFileId"], out var f) && f > 0 ? f : null;

            if (existing == null)
            {
                _config.Levels.Add(entry);
            }
        }

        _config.Save(Program.ConfigPath);
        ServeJson(context, new { ok = true });
    }

    /// <summary>Brings every other player to one of them.</summary>
    private void HandleGather(HttpListenerContext context, System.Collections.Specialized.NameValueCollection query)
    {
        if (!byte.TryParse(query["id"], out byte smallId))
        {
            ServeJson(context, new { ok = false, error = "Which player?" });
            return;
        }

        if (_server.Players.Get(smallId) is not { } target)
        {
            ServeJson(context, new { ok = false, error = "That player has left." });
            return;
        }

        if (!target.HasPosition)
        {
            ServeJson(context, new
            {
                ok = false,
                error = $"{target.DisplayName} has not reported a position yet.",
            });

            return;
        }

        var result = _server.GatherEveryoneTo(smallId);

        ServeJson(context, new
        {
            ok = true,
            moved = result.Moved,
            skipped = result.Skipped,
            name = target.DisplayName,
        });
    }

    /// <summary>
    /// Removes one entity from the world.
    ///
    /// A prop marked to come back is forgotten first. Without that it would
    /// vanish and then reappear the next time somebody joined, which looks like
    /// the button not working.
    /// </summary>
    private void HandleDespawn(HttpListenerContext context, System.Collections.Specialized.NameValueCollection query)
    {
        if (!ushort.TryParse(query["id"], out ushort id))
        {
            ServeJson(context, new { ok = false, error = "Which entity?" });
            return;
        }

        if (_server.Entities.Get(id) is not { } entity)
        {
            ServeJson(context, new { ok = false, error = "That entity is no longer in the world." });
            return;
        }

        string name = entity.ShortName;
        bool wasPersistent = entity.Persistent;

        if (wasPersistent)
        {
            _server.ForgetProp(id);
        }

        if (!_server.DespawnEntity(id))
        {
            ServeJson(context, new { ok = false, error = "That entity is no longer in the world." });
            return;
        }

        _server.Log("WARN", $"Panel removed '{name}'");

        ServeJson(context, new { ok = true, name, wasPersistent });
    }

    /// <summary>Marks a prop to be put back after a restart, or stops doing so.</summary>
    private void HandlePersist(HttpListenerContext context, System.Collections.Specialized.NameValueCollection query)
    {
        if (!ushort.TryParse(query["id"], out ushort id))
        {
            ServeJson(context, new { ok = false, error = "Which prop?" });
            return;
        }

        bool keep = query["keep"] != "0";

        bool done = keep
            ? _server.KeepProp(id, query["note"] ?? "")
            : _server.ForgetProp(id);

        ServeJson(context, new
        {
            ok = done,
            error = done ? null : "That prop is no longer in the world.",
        });
    }

    private void HandlePurge(HttpListenerContext context, NameValueCollection query)
    {
        if (!byte.TryParse(query["id"], out var smallId))
        {
            ServeJson(context, new { ok = false, error = "no such player" });
            return;
        }

        int removed = _server.PurgeEntitiesOf(smallId);
        _server.Log("WARN", $"Panel purged {removed} entities owned by SmallID {smallId}");

        ServeJson(context, new { ok = true, removed });
    }

    private void HandleRestart(HttpListenerContext context, NameValueCollection query)
    {
        string reason = query["reason"] is { Length: > 0 } r
            ? r
            : "Server is restarting, please wait a few minutes.";

        int grace = int.TryParse(query["grace"], out var g) ? g : 4;

        // Answer before the process goes away, or the panel only sees the socket drop.
        ServeJson(context, new { ok = true });

        _ = _server.RestartAsync(reason, grace);
    }

    // ---- settings ----

    private void HandleSettings(HttpListenerContext context, NameValueCollection query)
    {
        // Identity
        if (query["name"] is { Length: > 0 } name)
        {
            _config.ServerName = name;
        }

        if (query["description"] is { } description)
        {
            _config.Description = description;
        }

        if (int.TryParse(query["maxPlayers"], out var max) && max is > 0 and <= 255)
        {
            _config.MaxPlayers = max;
        }

        if (int.TryParse(query["privacy"], out var privacy) && privacy is >= 0 and <= 3)
        {
            _config.Privacy = privacy;
        }

        // Gameplay
        ReadBool(query, "nameTags", v => _config.NameTags = v);
        ReadBool(query, "voiceChat", v => _config.VoiceChat = v);
        ReadBool(query, "playerConstraining", v => _config.PlayerConstraining = v);
        ReadBool(query, "mortality", v => _config.Mortality = v);
        ReadBool(query, "friendlyFire", v => _config.FriendlyFire = v);
        ReadBool(query, "knockout", v => _config.Knockout = v);

        if (int.TryParse(query["knockoutLength"], out var knockout) && knockout is >= 0 and <= 600)
        {
            _config.KnockoutLength = knockout;
        }

        if (float.TryParse(query["maxAvatarHeight"], System.Globalization.CultureInfo.InvariantCulture,
                out var height) && height is > 0 and <= 100)
        {
            _config.MaxAvatarHeight = height;
        }

        if (int.TryParse(query["slowMoMode"], out var slowMo) && slowMo is >= 0 and <= 4)
        {
            _config.SlowMoMode = slowMo;
        }

        // Permission gates
        ReadLevel(query, "devTools", v => _config.DevTools = v);
        ReadLevel(query, "constrainer", v => _config.Constrainer = v);
        ReadLevel(query, "nimbus", v => _config.Nimbus = v);
        ReadLevel(query, "spawning", v => _config.Spawning = v);
        ReadLevel(query, "customAvatars", v => _config.CustomAvatars = v);
        ReadLevel(query, "kicking", v => _config.Kicking = v);
        ReadLevel(query, "banning", v => _config.Banning = v);
        ReadLevel(query, "teleportation", v => _config.Teleportation = v);

        // Limits
        if (int.TryParse(query["maxEntities"], out var maxEntities) && maxEntities is > 0 and <= 100000)
        {
            _config.MaxEntities = maxEntities;
        }

        ReadBool(query, "cullOrphans", v => _config.CullOrphanedEntities = v);


        // Crash protection
        ReadBool(query, "antiSpam", v => _config.AntiSpamEnabled = v);
        ReadLevel(query, "antiSpamExemptLevel", v => _config.AntiSpamExemptLevel = v);

        if (int.TryParse(query["spawnBurstLimit"], out var burst) && burst is >= 1 and <= 1000)
        {
            _config.SpawnBurstLimit = burst;
        }

        if (int.TryParse(query["spawnWindowSeconds"], out var win) && win is >= 1 and <= 120)
        {
            _config.SpawnWindowSeconds = win;
        }

        if (int.TryParse(query["maxEntitiesPerPlayer"], out var perPlayer) && perPlayer is >= 1 and <= 20000)
        {
            _config.MaxEntitiesPerPlayer = perPlayer;
        }

        if (int.TryParse(query["spamStrikes"], out var strikes) && strikes is >= 1 and <= 20)
        {
            _config.SpamStrikesBeforeKick = strikes;
        }

        // Zero is the off switch, so the floor is zero rather than a usable timeout.
        if (int.TryParse(query["ammoTimeoutSeconds"], out var ammo) && ammo is >= 0 and <= 86400)
        {
            _config.AmmoTimeoutSeconds = ammo;
        }

        if (int.TryParse(query["idleTimeoutSeconds"], out var idle) && idle is >= 0 and <= 86400)
        {
            _config.IdleTimeoutSeconds = idle;
        }

        if (int.TryParse(query["orphanTimeoutSeconds"], out var orphan) && orphan is >= 5 and <= 86400)
        {
            _config.OrphanTimeoutSeconds = orphan;
        }

        _config.Save(Program.ConfigPath);

        // Reaches players who are already connected, not just the browser listing.
        _server.PushSettings();

        _server.Log("INFO", "Settings updated from the panel");

        ServeJson(context, new { ok = true });
    }

    private static void ReadBool(NameValueCollection query, string key, Action<bool> apply)
    {
        if (bool.TryParse(query[key], out var value))
        {
            apply(value);
        }
    }

    private static void ReadLevel(NameValueCollection query, string key, Action<PermissionLevel> apply)
    {
        if (int.TryParse(query[key], out var raw))
        {
            apply(PermissionLevels.Clamp(raw));
        }
    }

    // ---- plumbing ----

    /// <summary>
    /// Lists, adds and removes panel accounts. Never returns a hash or a salt:
    /// the panel has no reason to see them and neither has anyone reading over
    /// a shoulder.
    /// </summary>
    private void HandleAccounts(HttpListenerContext context, NameValueCollection query)
    {
        string action = query["action"] ?? "list";
        string name = (query["name"] ?? "").Trim();

        switch (action)
        {
            case "add":
            {
                string password = query["password"] ?? "";

                if (name.Length == 0 || password.Length < 8)
                {
                    context.Response.StatusCode = 400;
                    Write(context, "text/plain",
                        "An account needs a name and a password of at least eight characters.");
                    return;
                }

                if (string.Equals(name, _config.DashboardUser, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 400;
                    Write(context, "text/plain",
                        "That name belongs to the panel's own account. Change it in the server settings instead.");
                    return;
                }

                Users.Set(name, password, PanelPermissions.ParseRole(query["role"]));
                Users.Save();
                _server.Log("INFO", $"Panel account {name} set by {_acting}");
                break;
            }

            case "remove":
            {
                if (Users.Remove(name))
                {
                    Users.Save();
                    _server.Log("INFO", $"Panel account {name} removed by {_acting}");
                }

                break;
            }
        }

        ServeJson(context, Users.Accounts
            .Select(a => new { name = a.Key, role = a.Value.Role, createdAt = a.Value.CreatedAt })
            .OrderBy(a => a.name)
            .ToArray());
    }

    private static void ServeJson(HttpListenerContext context, object payload)
    {
        Write(context, "application/json; charset=utf-8", JsonSerializer.Serialize(payload, Json));
    }

    private static void Write(HttpListenerContext context, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);

        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }
}
