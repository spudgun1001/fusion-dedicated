using FusionDedicated;
using FusionDedicated.Server;
using FusionDedicated.Commands;
using FusionDedicated.Commands.Rcon;
using FusionDedicated.Server.Audit;
using FusionDedicated.Server.Bans;
using FusionDedicated.Server.Ranks;
using FusionDedicated.Server.Safety;
using FusionDedicated.Web;
using Steamworks;

public static class Program
{
    public static string ConfigPath { get; private set; } = "server.json";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        ConfigPath = args.FirstOrDefault(a => a.EndsWith(".json")) ?? "server.json";

        var config = ServerConfig.Load(ConfigPath, out string? configError);

        if (configError != null)
        {
            Console.WriteLine($"WARNING: {ConfigPath} could not be read ({configError}).");
            Console.WriteLine("Running on defaults. The file is left untouched so you can fix it;");
            Console.WriteLine("every setting from the panel is being ignored until you do.");
        }
        else if (string.IsNullOrWhiteSpace(config.ServerCode))
        {
            config.ServerCode = LobbyPublisher.GenerateCode();
            config.Save(ConfigPath);
        }

        Banner(config);

        // ---- Steam ----
        // The app id comes from steam_appid.txt next to the binary; Fusion runs under
        // SteamVR's id rather than BONELAB's, and so does this.
        var steam = SteamStartup.TryInit(SteamAPI.Init);

        if (steam != SteamInitResult.Ok)
        {
            Console.WriteLine(SteamStartup.Explain(steam));
            return 2;
        }

        Console.WriteLine($"Steam: {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID().m_SteamID})");

        SteamNetworkingUtils.InitRelayNetworkAccess();

        // Callbacks must be pumped continuously or every await below — lobby creation
        // especially — never completes. The main loop takes over afterwards.
        using var startupPump = new CancellationTokenSource();

        var pumpTask = Task.Run(async () =>
        {
            while (!startupPump.IsCancellationRequested)
            {
                try { SteamAPI.RunCallbacks(); } catch { }
                await Task.Delay(16);
            }
        });

        // The relay config and certificate download asynchronously; connecting before
        // they land is what produces "Bad cert: CA key ... is not known to us".
        Console.WriteLine("Waiting for the Steam relay network...");
        await Task.Delay(4000);

        // ---- server ----
        using var server = new FusionServer(config)
        {
            HostPlatformId = SteamUser.GetSteamID().m_SteamID,
        };

        server.Start();

        // Minute rows live next to the logs, so graphs cover days rather than only
        // the couple of hours the in-memory ring holds.
        server.Resources.OpenStore(Path.IsPathRooted(config.LogDirectory)
            ? config.LogDirectory
            : Path.Combine(AppContext.BaseDirectory, config.LogDirectory));

        var safety = new SafetyListStore(Path.Combine(AppContext.BaseDirectory, "lists"));
        safety.LoadCache();
        server.SafetyLists = safety;

        var blocklist = new BlocklistStore(Path.Combine(AppContext.BaseDirectory, "blocklist.json"));
        blocklist.ReloadIfChanged();
        server.Blocklist = blocklist;

        server.RebuildBlocklist();

        if (blocklist.Current is { } rules)
        {
            server.Log("INFO", $"Blocklist: {rules.Barcodes.Count} barcodes, " +
                               $"{rules.Keywords.Count} keywords, " +
                               $"extended protection {(config.ExtendedProtection ? "on" : "off")}");
        }
        else
        {
            server.Log("WARN", "No blocklist.json — spawn rules come from the community list only.");
        }

        if (config.GlobalListsEnabled)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            await safety.RefreshAsync(async url =>
            {
                try { return await http.GetStringAsync(url); }
                catch { return null; }
            });

            server.RebuildBlocklist();
            server.Log("INFO", $"Safety lists: {safety.Mods?.Mods.Count ?? 0} blacklisted mods, " +
                               $"{safety.Bans?.Bans.Count ?? 0} global bans");
        }

        var bans = new BanStore(Path.Combine(AppContext.BaseDirectory, "bans.json"));
        bans.ReloadIfChanged();

        int migratedBans = bans.MigrateFrom(config.Bans);

        if (migratedBans > 0)
        {
            bans.Save();
            server.Log("INFO", $"Migrated {migratedBans} bans into bans.json");
        }

        server.BanList = bans;

        int lapsed = bans.SweepExpired();

        if (lapsed > 0)
        {
            bans.Save();
        }

        server.Log("INFO", $"Bans: {bans.Entries.Count} listed" +
                           (lapsed > 0 ? $", {lapsed} expired and lifted" : ""));

        var members = new Whitelist(Path.Combine(AppContext.BaseDirectory, "whitelist.json"))
        {
            Enabled = config.WhitelistEnabled,
        };

        members.ReloadIfChanged();
        server.Members = members;

        if (config.WhitelistEnabled)
        {
            server.Log("WARN", $"Whitelist is ON — {members.Entries.Count} players may join. " +
                               "An empty list locks everyone out, including you.");
        }

        server.AuditTrail = new AuditLog(Path.IsPathRooted(config.LogDirectory)
            ? config.LogDirectory
            : Path.Combine(AppContext.BaseDirectory, config.LogDirectory));

        var ranks = new RankStore(Path.Combine(AppContext.BaseDirectory, "ranks.json"));
        ranks.Load();

        int migrated = ranks.MigrateFrom(config.Permissions);

        string permissionListPath = Path.Combine(AppContext.BaseDirectory, "permissionList.xml");

        if (File.Exists(permissionListPath))
        {
            int imported = PermissionListImporter.Import(ranks, File.ReadAllText(permissionListPath));

            if (imported > 0)
            {
                server.Log("INFO", $"Imported {imported} ranks from permissionList.xml");
            }
        }

        int seededOwners = ranks.MergeSeed(
            ParseIds(Environment.GetEnvironmentVariable("OWNER_STEAMIDS")), PermissionLevel.Owner);

        int seededOperators = ranks.MergeSeed(
            ParseIds(Environment.GetEnvironmentVariable("OPERATOR_STEAMIDS")), PermissionLevel.Operator);

        if (migrated + seededOwners + seededOperators > 0)
        {
            ranks.Save();
        }

        server.Ranks = ranks;
        ranks.ReloadIfChanged();

        using var rankWatcher = new RankFileWatcher(ranks, message => server.Log("INFO", message));
        rankWatcher.Start();

        server.Log("INFO", $"Ranks: {ranks.Entries.Count} players listed");

        var commands = new CommandProcessor(new ServerCommandTarget(server));

        // ---- lobby ----
        using var lobby = new LobbyPublisher();

        if (await lobby.PublishAsync(config.MaxPlayers))
        {
            lobby.Update(config, server.Players.Players, SteamUser.GetSteamID().m_SteamID);
            server.Log("INFO", $"Lobby published: {lobby.LobbyId} — the server is visible in the browser");
        }
        else
        {
            server.Log("ERROR", "Could not create the Steam lobby — the server is invisible in the browser");
        }

        // ---- dashboard ----
        var dashboard = new Dashboard(server, config, lobby);

        try
        {
            dashboard.Start();

            if (dashboard.IsListening)
            {
                server.Log("INFO", $"Control panel: {dashboard.Url}");
            }
        }
        catch (Exception ex)
        {
            server.Log("ERROR", $"Control panel failed to start ({ex.Message}). " +
                                $"Port {config.DashboardPort} is busy, or this needs elevated rights.");
        }

        startupPump.Cancel();
        await pumpTask;

        Console.WriteLine();
        Console.WriteLine($"  Panel:   {dashboard.Url}");
        Console.WriteLine($"  Code:    {config.ServerCode}");
        Console.WriteLine($"  Ctrl+C   to stop");
        Console.WriteLine();

        // ---- main loop ----
        using var quit = new CancellationTokenSource();

        StdinCommands.Start(commands, Console.WriteLine, quit.Token);

        using var rcon = new RconServer(commands, config.RconPassword, config.RconPort, server.Log);

        rcon.Start();

        if (rcon.Port != 0)
        {
            server.Log("INFO", $"RCON listening on port {rcon.Port}");
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Cancel();
        };

        // A restart re-launches this same process, so the panel's button works no
        // matter how the server was started — no wrapper script required.
        var restarting = false;

        server.RestartRequested += () =>
        {
            restarting = true;
            quit.Cancel();
        };

        var lastLobbyUpdate = DateTime.UtcNow;
        var lastTick = DateTime.UtcNow;
        var lastSample = DateTime.UtcNow;

        while (!quit.IsCancellationRequested)
        {
            SteamAPI.RunCallbacks();

            server.Receive();

            if ((DateTime.UtcNow - lastLobbyUpdate).TotalSeconds >= 5)
            {
                lobby.Update(config, server.Players.Players, SteamUser.GetSteamID().m_SteamID);
                lastLobbyUpdate = DateTime.UtcNow;
            }

            if ((DateTime.UtcNow - lastTick).TotalSeconds >= 10)
            {
                if (blocklist.ReloadIfChanged())
                {
                    server.RebuildBlocklist();
                    server.Log("INFO", "Reloaded blocklist.json");
                }

                if (bans.ReloadIfChanged())
                {
                    server.Log("INFO", $"Reloaded bans.json — {bans.Entries.Count} listed");
                }

                if (members.ReloadIfChanged())
                {
                    server.Log("INFO", $"Reloaded whitelist.json — {members.Entries.Count} listed");
                }

                if (bans.SweepExpired() > 0)
                {
                    bans.Save();
                }

                server.Tick();
                lastTick = DateTime.UtcNow;
            }

            if ((DateTime.UtcNow - lastSample).TotalSeconds >= 5)
            {
                server.Resources.Sample_(server);
                lastSample = DateTime.UtcNow;
            }

            await Task.Delay(16);
        }

        // ---- shutdown ----
        Console.WriteLine();
        server.Log("INFO", "Shutting down...");

        foreach (var player in server.Players.Players)
        {
            server.Kick(player.SmallId, "Server shutting down");
        }

        await Task.Delay(400);

        dashboard.Stop();
        lobby.Close();

        SteamAPI.Shutdown();

        if (restarting)
        {
            Console.WriteLine("Restarting...");
            Relaunch();
            return 0;
        }

        Console.WriteLine("Stopped.");
        return 0;
    }

    /// <summary>
    /// Starts a fresh copy of this server and lets this one exit. The child is
    /// deliberately not tied to our stdio, so it survives the parent going away.
    /// </summary>
    private static IEnumerable<ulong> ParseIds(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
        {
            yield break;
        }

        foreach (string part in commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ulong.TryParse(part.Trim(), out ulong id))
            {
                yield return id;
            }
        }
    }

    private static void Relaunch()
    {
        // Under systemd the supervisor restarts us, so spawning our own copy would
        // leave two servers fighting over the same lobby and panel port. systemd
        // sets INVOCATION_ID for every unit it starts, which is how we can tell.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID")))
        {
            Console.WriteLine("Running under systemd — exiting and letting it restart us.");
            return;
        }

        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "dotnet",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
            };

            // Environment.ProcessPath is the dotnet host, so the dll has to come back
            // as an argument when the app is not published as a native executable.
            var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;

            if (!string.IsNullOrEmpty(assembly) && assembly.EndsWith(".dll"))
            {
                info.ArgumentList.Add(assembly);
            }

            info.ArgumentList.Add(ConfigPath);

            System.Diagnostics.Process.Start(info);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not relaunch automatically: {ex.Message}");
        }
    }

    private static void Banner(ServerConfig config)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════════════════════╗");
        Console.WriteLine("  ║   FUSION DEDICATED — headless relay server    ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"  Name:    {config.ServerName}");
        Console.WriteLine($"  Level:   {config.LevelTitle}");
        Console.WriteLine($"  Version: v{config.Version}");
        Console.WriteLine($"  Slots:   {config.MaxPlayers}");
        Console.WriteLine();
    }
}
