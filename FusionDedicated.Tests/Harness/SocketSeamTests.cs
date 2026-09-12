using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Tests.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>The server reaches Steam's sockets only through a transport, so tests can stand in for Steam.</summary>
public class SocketSeamTests
{
    [Fact]
    public void The_server_makes_no_direct_steam_socket_call()
    {
        string source = FusionServerSource.Text();

        Assert.DoesNotContain("SteamNetworkingSockets.", source);
        Assert.DoesNotContain("SteamUser.", source);
    }

    [Fact]
    public void A_player_who_asks_to_join_is_answered_through_the_transport()
    {
        var transport = new FakeTransport();
        using var server = new FusionServer(new ServerConfig(), transport);
        server.Start();

        var connection = transport.Connect();
        transport.Deliver(connection, FusionProtocol.BuildConnectionRequest(
            76561198000000001, new Version(server.Config.VersionMajor, server.Config.VersionMinor, 0),
            "SLZ.BONELAB.Content.Avatar.FordBW",
            new Dictionary<string, string> { ["Username"] = "Joel" }, new List<string>()));

        server.Receive();

        Assert.NotNull(server.Players.GetByPlatformId(76561198000000001));
        Assert.Contains(transport.SentTo(connection), sent => sent.Message[0] == FusionProtocol.TagConnectionResponse);
    }

    [Fact]
    public void A_failed_send_does_not_count_as_sent()
    {
        var transport = new FailingSendTransport();
        using var server = new FusionServer(new ServerConfig(), transport);
        server.Start();

        server.SendTo(new Steamworks.HSteamNetConnection(1), new byte[] { 1, 2, 3 }, reliable: true);

        Assert.Equal(0L, server.PacketsOut);
        Assert.Equal(0L, server.BytesOut);
    }

    /// <summary>A transport whose send always fails, for the counter test above.</summary>
    private sealed class FailingSendTransport : ISocketTransport
    {
        public ulong LocalSteamId => 0;

        public void Start(Action<Steamworks.HSteamNetConnection> connecting,
            Action<Steamworks.HSteamNetConnection, string> closed)
        {
        }

        public void Accept(Steamworks.HSteamNetConnection connection)
        {
        }

        public void Close(Steamworks.HSteamNetConnection connection, string? reason)
        {
        }

        public ulong RemoteSteamId(Steamworks.HSteamNetConnection connection) => 0;

        public bool Send(Steamworks.HSteamNetConnection connection, byte[] message, bool reliable) => false;

        public int Receive(int max, Action<Steamworks.HSteamNetConnection, byte[]> handle) => 0;

        public void Dispose()
        {
        }
    }
}
