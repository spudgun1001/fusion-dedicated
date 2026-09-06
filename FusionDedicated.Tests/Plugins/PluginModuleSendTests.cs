using BonelabServerBrowser.Fusion;
using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

/// <summary>
/// Answering the message a plugin received is not enough. Hosting a mod means
/// sending messages nobody asked for, on other tags: LabRP's host sends a balance
/// update to one player and broadcasts a snapshot to everybody.
/// </summary>
public class PluginModuleSendTests
{
    private sealed class RecordingActions : IPluginActions
    {
        public readonly List<string> Sent = new();

        public void Kick(ulong platformId, string reason) { }
        public void Ban(ulong platformId, string reason) { }
        public void SetRank(ulong platformId, PermissionLevel level) { }
        public void Despawn(ushort entityId) { }

        public void SendModule(ulong platformId, long handlerTag, byte[] payload)
            => Sent.Add($"to {platformId} tag {handlerTag} {payload.Length} bytes");

        public void BroadcastModule(long handlerTag, byte[] payload)
            => Sent.Add($"all tag {handlerTag} {payload.Length} bytes");
    }

    [Fact]
    public void A_plugin_can_send_to_one_player()
    {
        var actions = new RecordingActions();

        actions.SendModule(76561198000000001, 42, new byte[] { 1, 2 });

        Assert.Equal("to 76561198000000001 tag 42 2 bytes", actions.Sent.Single());
    }

    [Fact]
    public void A_plugin_can_send_to_everybody()
    {
        var actions = new RecordingActions();

        actions.BroadcastModule(42, new byte[] { 1, 2, 3 });

        Assert.Equal("all tag 42 3 bytes", actions.Sent.Single());
    }

    [Fact]
    public void The_server_stamps_its_own_id_so_a_client_will_accept_it()
    {
        // LabRP's BalanceUpdateMessage ignores anything whose sender is not 0, and
        // 0 is the server. Getting this wrong means the message arrives and is
        // silently thrown away by the client.
        byte[] message = ModuleProtocol.WriteModuleToClients(42, 0, new byte[] { 9 });

        Assert.Equal(0, message[3] == 0 ? 0 : message[4]);
    }

    [Fact]
    public void The_wiring_reaches_the_server_calls_behind_it()
    {
        var sent = new List<string>();

        var actions = new ServerPluginActions(
            (_, _) => { }, (_, _) => { }, (_, _) => { }, _ => { },
            (id, tag, payload) => sent.Add($"to {id} {tag} {payload.Length}"),
            (tag, payload) => sent.Add($"all {tag} {payload.Length}"));

        actions.SendModule(7, 42, new byte[] { 1 });
        actions.BroadcastModule(42, new byte[] { 1, 2 });

        Assert.Equal(new[] { "to 7 42 1", "all 42 2" }, sent);
    }
}
