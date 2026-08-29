using FusionDedicated;
using FusionDedicated.Server;
using FusionDedicated.Web;
using Steamworks;

public static class Program
{
    public static string ConfigPath { get; private set; } = "server.json";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        ConfigPath = args.FirstOrDefault(a => a.EndsWith(".json")) ?? "server.json";

        var config = ServerConfig.Load(ConfigPath);

        if (string.IsNullOrWhiteSpace(config.ServerCode))
        {
            config.ServerCode = LobbyPublisher.GenerateCode();
            config.Save(ConfigPath);
        }

        Banner(config);

        // ---- Steam ----
        // The app id comes from steam_appid.txt next to the binary; Fusion runs under
        // SteamVR's id rather than BONELAB's, and so does this.
        if (!SteamAPI.Init())
        {
            Console.WriteLine("Steam unavailable: SteamAPI.Init() returned false.");
            Console.WriteLine("Check that the Steam client is running and signed in, " +
                              "and that steam_appid.txt sits next to the binary.");
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
