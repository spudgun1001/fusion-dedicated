using FusionDedicated.Plugins;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A real server with the real plugin host, loading real plugin assemblies off disk.
///
/// Everything Program.cs hangs off the server is wired here the same way, so a test
/// drives the whole chain a level talks to rather than a stand-in for it.
/// </summary>
public sealed class LevelRig : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fusion-level-" + Guid.NewGuid().ToString("N"));

    private readonly PluginHost _host;

    /// <param name="saved">A plugin's data file as JSON, by plugin name, for state a run would already have.</param>
    public LevelRig(ServerConfig config, string[] plugins, IReadOnlyDictionary<string, string>? saved = null)
    {
        World = new World(config);

        string folder = Path.Combine(_root, "plugins");
        string data = Path.Combine(_root, "plugin-data");

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(data);

        foreach (string plugin in plugins)
        {
            Copy(Path.Combine(PluginSource.Directory, plugin), Path.Combine(folder, plugin));
        }

        foreach (var (plugin, json) in saved ?? new Dictionary<string, string>())
        {
            File.WriteAllText(Path.Combine(data, plugin + ".json"), json);
        }

        var server = World.Server;
        var health = new PluginHealth();

        Action<string, string> log = (level, message) => server.Log(level, message);

        var events = new PluginEvents(health, log);
        var panel = new PluginPanel(health, log);
        var modules = new PluginModules(health, log);
        var bus = new PluginBus(health, log);

        var rpc = new PluginRpc(health, log)
        {
            Sender = (kind, path, value, platformId) => server.SendRpc(kind, path, value, platformId),
        };

        var world = new PluginWorld
        {
            Lookup = server.FindEntity,
            Everything = server.AllEntities,
            MotionLookup = server.FindMotion,
            HoldersLookup = entityId => PluginOccupancy.Holders(
                server.HoldersOf(entityId),
                smallId => server.Players.Get(smallId)?.PlatformId,
                smallId => server.Players.Get(smallId) is { HasPosition: true } holder
                    ? (holder.LastPosition.X, holder.LastPosition.Y, holder.LastPosition.Z)
                    : null,
                server.Entities.Get(entityId) is { PositionKnown: true } entity
                    ? (entity.X, entity.Y, entity.Z)
                    : ((float X, float Y, float Z)?)null),
            HolsteredLookup = server.HolsteredBy,
            HeldLookup = server.HeldBy,
            SeatOfLookup = server.SeatOfPlayer,
            SceneEntityLookup = server.SceneEntityOf,
        };

        var actions = new ServerPluginActions(
            (id, reason) => server.OnLoop(() =>
            {
                if (server.Players.GetByPlatformId(id) is { } target)
                {
                    server.Kick(target.SmallId, reason);
                }
            }),
            (id, reason) => server.OnLoop(() => server.Ban(id, "", reason)),
            (id, level) => server.OnLoop(() => server.SetPermission(id, "", level)),
            id => server.RemoveEntity(id),
            (id, tag, payload) => server.SendModuleTo(id, tag, payload),
            (tag, payload) => server.BroadcastModule(tag, payload),
            (barcode, x, y, z, rotation) => server.SpawnForPlugin(barcode, x, y, z, rotation),
            (id, note) => server.KeepProp(id, note),
            id => server.ForgetProp(id),
            (id, platformId) => server.GiveOwner(id, platformId),
            (barcode, x, y, z, rotation, platformId) => server.SpawnForPlayer(barcode, x, y, z, rotation, platformId),
            (id, platformId, index) => server.HolsterForPlugin(id, platformId, index),
            platformId => server.UnseatForPlugin(platformId));

        _host = new PluginHost(folder, data, events, health, panel, modules, rpc, bus, world, actions,
            () => server.Players.Players.Select(PluginPlayers.Snapshot).ToList(),
            log);

        server.Plugins = events;
        server.PluginModules = modules;
        server.PluginRpc = rpc;

        Started = server.Exclusive(() => _host.LoadAll());
    }

    public World World { get; }

    public FusionServer Server => World.Server;

    /// <summary>How many plugins started. A test asserts this before anything else.</summary>
    public int Started { get; }

    public IReadOnlyList<string> Loaded => _host.Loaded.Select(p => p.Name + " " + p.Manifest.Version).ToList();

    /// <summary>Every server log line, which is where a plugin says what it did.</summary>
    public IReadOnlyList<string> Log => Server.RecentLog(2000).Select(e => e.Message).ToList();

    public int LinesSaying(string text)
        => Log.Count(line => line.Contains(text, StringComparison.Ordinal));

    public void Dispose()
    {
        foreach (var plugin in _host.Loaded)
        {
            try { _host.Unload(plugin.Name); } catch { }
        }

        World.Dispose();

        try { Directory.Delete(_root, true); } catch { }
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (string file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        }
    }
}

/// <summary>Where the built plugins are, which is a sibling checkout rather than part of this one.</summary>
public static class PluginSource
{
    /// <summary>The mods checkout, whether or not anything has been built in it.</summary>
    public static string? Repository { get; } = Find();

    public static string Directory => Path.Combine(Repository ?? "", "out");

    /// <summary>Whether a plugin of that name has been built. A missing build is a failure, not a skip.</summary>
    public static bool Has(string plugin)
        => Repository != null && File.Exists(Path.Combine(Directory, plugin, "plugin.json"));

    private static string? Find()
    {
        string? set = Environment.GetEnvironmentVariable("FUSION_SERVER_MODS");

        if (!string.IsNullOrWhiteSpace(set) && System.IO.Directory.Exists(set))
        {
            return set;
        }

        var here = new DirectoryInfo(AppContext.BaseDirectory);

        while (here != null)
        {
            string guess = Path.Combine(here.FullName, "fusion-server-mods");

            if (System.IO.Directory.Exists(guess))
            {
                return guess;
            }

            here = here.Parent;
        }

        return null;
    }
}
