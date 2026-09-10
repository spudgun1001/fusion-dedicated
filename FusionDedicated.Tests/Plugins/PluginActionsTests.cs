using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// The actions a plugin may take are a thin wrapper over what the panel already
/// does, so this pins the wiring rather than the behaviour underneath.
/// </summary>
public class PluginActionsTests
{
    [Fact]
    public void Each_action_reaches_the_server_call_behind_it()
    {
        var made = new List<string>();

        var actions = new ServerPluginActions(
            (id, reason) => made.Add($"kick {id} {reason}"),
            (id, reason) => made.Add($"ban {id} {reason}"),
            (id, level) => made.Add($"rank {id} {level}"),
            id => made.Add($"despawn {id}"),
            (id, tag, payload) => made.Add($"send {id}"),
            (tag, payload) => made.Add($"broadcast {tag}"));

        actions.Kick(1, "afk");
        actions.Ban(2, "nuke");
        actions.SetRank(3, PermissionLevel.Operator);
        actions.Despawn(400);

        Assert.Equal(
            new[] { "kick 1 afk", "ban 2 nuke", "rank 3 Operator", "despawn 400" },
            made);
    }

    [Fact]
    public void Spawn_keep_and_forget_reach_the_server_calls_behind_them()
    {
        var made = new List<string>();

        var actions = new ServerPluginActions(
            (id, reason) => { },
            (id, reason) => { },
            (id, level) => { },
            id => { },
            (id, tag, payload) => { },
            (tag, payload) => { },
            (barcode, x, y, z, rotation) =>
            {
                made.Add($"spawn {barcode} {x} {y} {z} {rotation.Length}");
                return 512;
            },
            (id, note) =>
            {
                made.Add($"keep {id} {note}");
                return true;
            },
            id =>
            {
                made.Add($"forget {id}");
                return true;
            });

        Assert.Equal((ushort)512, actions.Spawn("a.b.Door", 1f, 2f, 3f, new byte[7]));
        Assert.True(actions.Keep(512, "D1"));
        Assert.True(actions.Forget(400));

        Assert.Equal(new[] { "spawn a.b.Door 1 2 3 7", "keep 512 D1", "forget 400" }, made);
    }

    [Fact]
    public void Actions_built_without_them_refuse_spawn_keep_and_forget()
    {
        var actions = new ServerPluginActions(
            (id, reason) => { },
            (id, reason) => { },
            (id, level) => { },
            id => { },
            (id, tag, payload) => { },
            (tag, payload) => { });

        Assert.Equal((ushort)0, actions.Spawn("a.b.Door", 0f, 0f, 0f, new byte[7]));
        Assert.False(actions.Keep(300, ""));
        Assert.False(actions.Forget(300));
    }

    [Fact]
    public void An_older_actions_class_gets_refusals_from_the_interface()
    {
        IPluginActions actions = new OlderActions();

        Assert.Equal((ushort)0, actions.Spawn("a.b.Door", 0f, 0f, 0f, new byte[7]));
        Assert.False(actions.Keep(300, ""));
        Assert.False(actions.Forget(300));
    }

    private sealed class OlderActions : IPluginActions
    {
        public void Kick(ulong platformId, string reason) { }
        public void Ban(ulong platformId, string reason) { }
        public void SetRank(ulong platformId, PermissionLevel level) { }
        public void Despawn(ushort entityId) { }
        public void SendModule(ulong platformId, long handlerTag, byte[] payload) { }
        public void BroadcastModule(long handlerTag, byte[] payload) { }
    }
}
