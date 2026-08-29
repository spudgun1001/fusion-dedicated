using System.Net.Sockets;
using FusionDedicated.Commands;
using FusionDedicated.Commands.Rcon;

namespace FusionDedicated.Tests.Commands;

public class RconServerTests : IDisposable
{
    private readonly RconServer _server;
    private readonly FakeTarget _target = new();

    public RconServerTests()
    {
        _server = new RconServer(new CommandProcessor(_target), "hunter2", 0, (_, _) => { });
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    private static async Task SendAsync(NetworkStream stream, RconPacket packet)
        => await stream.WriteAsync(RconCodec.Encode(packet));

    private static async Task<RconPacket> ReceiveAsync(NetworkStream stream)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header);

        int total = RconCodec.RequiredLength(header);
        var whole = new byte[total];

        header.CopyTo(whole, 0);
        await stream.ReadExactlyAsync(whole.AsMemory(4, total - 4));

        return RconCodec.Decode(whole);
    }

    private async Task<NetworkStream> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", _server.Port);
        return client.GetStream();
    }

    [Fact]
    public async Task A_correct_password_authenticates_and_echoes_the_id()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(99, RconPacketType.Auth, "hunter2"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(99, reply.Id);
        Assert.Equal(RconPacketType.ExecCommandOrAuthResponse, reply.Type);
    }

    [Fact]
    public async Task A_wrong_password_replies_with_minus_one()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(99, RconPacketType.Auth, "wrong"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(-1, reply.Id);
    }

    [Fact]
    public async Task A_command_before_authentication_is_refused()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(1, RconPacketType.ExecCommandOrAuthResponse, "players"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(-1, reply.Id);
        Assert.Empty(_target.Ranks);
    }

    [Fact]
    public async Task An_authenticated_command_runs_and_returns_its_output()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(1, RconPacketType.Auth, "hunter2"));
        await ReceiveAsync(stream);

        _target.Roster.Add(new CommandPlayer(76561198000000000, 1, "Spudgun", PermissionLevel.Default, 0));

        await SendAsync(stream, new RconPacket(2, RconPacketType.ExecCommandOrAuthResponse, "players"));
        var reply = await ReceiveAsync(stream);

        Assert.Equal(2, reply.Id);
        Assert.Contains("Spudgun", reply.Body);
    }

    [Fact]
    public async Task Promote_over_rcon_reaches_the_target()
    {
        await using var stream = await ConnectAsync();

        await SendAsync(stream, new RconPacket(1, RconPacketType.Auth, "hunter2"));
        await ReceiveAsync(stream);

        await SendAsync(stream, new RconPacket(2, RconPacketType.ExecCommandOrAuthResponse,
            "promote 76561198000000000 owner"));
        await ReceiveAsync(stream);

        Assert.Equal(PermissionLevel.Owner, _target.Ranks.Single().Level);
    }

    [Fact]
    public async Task Two_clients_are_served_independently()
    {
        await using var first = await ConnectAsync();
        await using var second = await ConnectAsync();

        await SendAsync(first, new RconPacket(1, RconPacketType.Auth, "hunter2"));
        await SendAsync(second, new RconPacket(2, RconPacketType.Auth, "wrong"));

        Assert.Equal(1, (await ReceiveAsync(first)).Id);
        Assert.Equal(-1, (await ReceiveAsync(second)).Id);
    }

    [Fact]
    public void An_empty_password_refuses_to_listen()
    {
        using var refused = new RconServer(new CommandProcessor(_target), "", 0, (_, _) => { });

        refused.Start();

        Assert.Equal(0, refused.Port);
    }
}
