using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginContextTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-plugincontext-" + Guid.NewGuid().ToString("N"));

    private readonly List<string> _log = new();

    public PluginContextTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class RecordingActions : IPluginActions
    {
        public readonly List<string> Done = new();

        public void Kick(ulong platformId, string reason) => Done.Add($"kick {platformId} {reason}");
        public void Ban(ulong platformId, string reason) => Done.Add($"ban {platformId} {reason}");
        public void SetRank(ulong platformId, PermissionLevel level) => Done.Add($"rank {platformId} {level}");
        public void Despawn(ushort entityId) => Done.Add($"despawn {entityId}");
    }

    private PluginContext Context(IPluginActions? actions = null)
        => new("police",
            new PluginEvents(new PluginHealth(), (_, _) => { }),
            new PluginStore(Path.Combine(_dir, "data.json")),
            new PluginPanel(new PluginHealth(), (_, _) => { }),
            new PluginModules(new PluginHealth(), (_, _) => { }),
            actions ?? new RecordingActions(),
            (level, message) => _log.Add(level + " " + message));

    [Fact]
    public void A_plugins_log_line_is_marked_with_its_name()
    {
        Context().Log("INFO", "started");

        Assert.Contains(_log, line => line.Contains("police") && line.Contains("started"));
    }

    [Fact]
    public void A_plugin_can_reach_its_own_store()
    {
        var context = Context();
        context.Store.Set("count", 3);

        Assert.Equal(3, context.Store.Get<int>("count"));
    }

    [Fact]
    public void A_plugin_can_ask_for_an_action()
    {
        var actions = new RecordingActions();
        Context(actions).Actions.Kick(7, "not police");

        Assert.Contains("kick 7 not police", actions.Done);
    }

    [Fact]
    public void The_context_carries_the_plugins_name()
    {
        Assert.Equal("police", Context().Name);
    }
}
