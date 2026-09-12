using Steamworks;

namespace FusionDedicated.Tests.Harness;

/// <summary>FakeTransport's own behaviour, separate from the socket seam contract in SocketSeamTests.</summary>
public class FakeTransportTests
{
    [Fact]
    public void Close_drops_the_closed_connections_queued_messages()
    {
        var transport = new FakeTransport();
        var a = transport.Connect();
        var b = transport.Connect();

        transport.Deliver(a, new byte[] { 1 });
        transport.Deliver(a, new byte[] { 2 });
        transport.Deliver(b, new byte[] { 3 });

        transport.Close(a, "test");

        var received = new List<(HSteamNetConnection Connection, byte[] Message)>();
        transport.Receive(10, (connection, message) => received.Add((connection, message)));

        var only = Assert.Single(received);
        Assert.Equal(b.m_HSteamNetConnection, only.Connection.m_HSteamNetConnection);
        Assert.Equal(new byte[] { 3 }, only.Message);
    }
}
