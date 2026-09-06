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
            id => made.Add($"despawn {id}"));

        actions.Kick(1, "afk");
        actions.Ban(2, "nuke");
        actions.SetRank(3, PermissionLevel.Operator);
        actions.Despawn(400);

        Assert.Equal(
            new[] { "kick 1 afk", "ban 2 nuke", "rank 3 Operator", "despawn 400" },
            made);
    }
}
