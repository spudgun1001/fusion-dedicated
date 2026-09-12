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

    [Fact]
    public void A_failed_broadcast_does_not_count_as_bytes_out()
    {
        var transport = new FailingSendTransport();
        using var server = new FusionServer(new ServerConfig(), transport);
        server.Start();

        var player = new ConnectedPlayer
        {
            Connection = new Steamworks.HSteamNetConnection(1),
            PlatformId = 76561198000000001UL,
            SmallId = 1,
        };
        server.Players.Add(player);

        server.Broadcast(new byte[] { 1, 2, 3 }, reliable: true);

        Assert.Equal(0L, player.BytesOut);
    }

    [Fact]
    public void The_steam_transport_accepts_a_read_failure_callback()
    {
        string source = SteamSocketTransportSource();

        Assert.Contains("Action<string>? onReadFailure", source);
    }

    [Fact]
    public void Receive_reports_an_unreadable_message_through_the_callback()
    {
        string source = SteamSocketTransportSource();

        Assert.Contains("_onReadFailure?.Invoke(", source);
    }

    [Fact]
    public void Receive_grows_its_buffer_to_the_requested_batch_size_instead_of_capping_it()
    {
        string source = SteamSocketTransportSource();

        Assert.DoesNotContain("Math.Min(max, _messageBuffer.Length)", source);
    }

    [Fact]
    public void The_constructor_logs_unreadable_packets_and_sets_the_registry_clock()
    {
        string ctor = FusionServerSource.Method("public FusionServer(");

        Assert.Contains("new SteamSocketTransport(", ctor);
        Assert.Contains("Failed to read a packet", ctor);
        Assert.Contains("Entities.Clock =", ctor);
    }

    /// <summary>Reads SteamSocketTransport.cs for the same reason FusionServerSource reads FusionServer.cs.</summary>
    private static string SteamSocketTransportSource()
        => File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FusionDedicated", "Server", "SteamSocketTransport.cs"))
            .Replace("\r\n", "\n");

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
