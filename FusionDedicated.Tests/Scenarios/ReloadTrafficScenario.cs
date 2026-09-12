using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Tests.Harness;
using Xunit.Abstractions;

namespace FusionDedicated.Tests.Scenarios;

/// <summary>Scenario 6: what a phones plugin reload sends, 47 payphones told their state once no matter how many times the reload repeats it.</summary>
public class ReloadTrafficScenario(ITestOutputHelper output)
{
    [Fact]
    public void A_reload_announcing_47_payphones_is_measured()
    {
        using var world = new World();
        var players = Enumerable.Range(1, 6)
            .Select(i => world.Join(76561198000000000 + (ulong)i, $"P{i}"))
            .ToList();

        players.ForEach(p => p.FinishLoading());

        var rpc = new PluginRpc(new PluginHealth(), (_, _) => { })
        {
            Sender = (kind, path, value, platformId) => world.Server.SendRpc(kind, path, value, platformId),
        };

        var before = players.ToDictionary(p => p.Name, p => world.Transport.SentTo(p.Connection).Count);

        for (var announce = 0; announce < 5; announce++)
        {
            for (ushort phone = 300; phone < 347; phone++)
            {
                rpc.SetString(RpcProtocol.PathFor(phone, 0), "412");
                rpc.SetString(RpcProtocol.PathFor(phone, 1), $"c{phone}");
                rpc.SetBool(RpcProtocol.PathFor(phone, 2), false);
                rpc.SetBool(RpcProtocol.PathFor(phone, 3), true);
                rpc.SetBool(RpcProtocol.PathFor(phone, 4), true);
                rpc.SetBool(RpcProtocol.PathFor(phone, 5), true);
                rpc.SetString(RpcProtocol.PathFor(phone, 6), "---");
            }

            world.Advance(TimeSpan.FromSeconds(5));
        }

        foreach (var player in players)
        {
            var rpcs = world.Transport.SentTo(player.Connection)
                .Skip(before[player.Name])
                .Where(m => m.Message[0] is >= 209 and <= 214)
                .ToList();

            output.WriteLine($"{player.Name}: {rpcs.Count} messages, {rpcs.Sum(m => m.Message.Length)} bytes");

            // Only the first announce sends anything; the four repeats carry values every client already has.
            Assert.Equal(47 * 7, rpcs.Count);
        }
    }
}
