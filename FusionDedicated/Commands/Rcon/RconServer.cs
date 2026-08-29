using System.Net;
using System.Net.Sockets;
using FusionDedicated.Web;

namespace FusionDedicated.Commands.Rcon;

/// <summary>
/// Source RCON over TCP, so rcon-cli, BadgerPanel and Discord bots can drive the
/// same commands the console does. Refuses to listen without a password, because
/// an unauthenticated RCON port is a remote shell over the server.
/// </summary>
public sealed class RconServer : IDisposable
{
    private readonly CommandProcessor _processor;
    private readonly string _password;
    private readonly int _requestedPort;
    private readonly Action<string, string> _log;
    private readonly CancellationTokenSource _stop = new();

    private TcpListener? _listener;

    public RconServer(CommandProcessor processor, string password, int port, Action<string, string> log)
    {
        _processor = processor;
        _password = password;
        _requestedPort = port;
        _log = log;
    }

    /// <summary>The bound port, or 0 when RCON is not listening.</summary>
    public int Port { get; private set; }

    public void Start()
    {
        if (string.IsNullOrEmpty(_password))
        {
            _log("INFO", "RCON is off: no password is set.");
            return;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, _requestedPort);
            _listener.Start();

            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }
        catch (Exception ex)
        {
            _log("ERROR", $"RCON could not listen on port {_requestedPort}: {ex.Message}");
            return;
        }

        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested && _listener != null)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var authenticated = false;

            try
            {
                await using var stream = client.GetStream();

                while (!_stop.IsCancellationRequested)
                {
                    var packet = await ReadPacketAsync(stream);

                    if (packet is not { } request)
                    {
                        return;
                    }

                    if (request.Type == RconPacketType.Auth)
                    {
                        authenticated = DashboardAuth.ConstantTimeEquals(request.Body, _password);

                        await WriteAsync(stream, new RconPacket(
                            authenticated ? request.Id : -1,
                            RconPacketType.ExecCommandOrAuthResponse,
                            ""));

                        if (!authenticated)
                        {
                            _log("WARN", "RCON authentication failed; closing the connection.");
                            return;
                        }

                        continue;
                    }

                    if (!authenticated)
                    {
                        await WriteAsync(stream, new RconPacket(-1, RconPacketType.Response, ""));
                        return;
                    }

                    string reply = _processor.Execute(request.Body);

                    _log("INFO", $"RCON: {request.Body}");

                    await WriteAsync(stream, new RconPacket(request.Id, RconPacketType.Response, reply));
                }
            }
            catch (Exception ex)
            {
                _log("WARN", $"RCON connection ended: {ex.Message}");
            }
        }
    }

    private static async Task<RconPacket?> ReadPacketAsync(NetworkStream stream)
    {
        var header = new byte[4];

        try
        {
            await stream.ReadExactlyAsync(header);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        int total = RconCodec.RequiredLength(header);
        var whole = new byte[total];

        header.CopyTo(whole, 0);
        await stream.ReadExactlyAsync(whole.AsMemory(4, total - 4));

        return RconCodec.Decode(whole);
    }

    private static async Task WriteAsync(NetworkStream stream, RconPacket packet)
        => await stream.WriteAsync(RconCodec.Encode(packet));

    public void Stop()
    {
        _stop.Cancel();

        try { _listener?.Stop(); } catch { }

        Port = 0;
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }
}
