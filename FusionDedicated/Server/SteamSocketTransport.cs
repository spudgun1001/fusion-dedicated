using System.Runtime.InteropServices;
using Steamworks;

namespace FusionDedicated.Server;

/// <summary>The real transport: Steam's networking sockets over the relay.</summary>
public sealed class SteamSocketTransport : ISocketTransport
{
    private readonly IntPtr[] _messageBuffer = new IntPtr[128];

    private HSteamListenSocket _listenSocket;
    private HSteamNetPollGroup _pollGroup;
    private Callback<SteamNetConnectionStatusChangedCallback_t>? _statusCallback;

    private Action<HSteamNetConnection> _connecting = _ => { };
    private Action<HSteamNetConnection, string> _closed = (_, _) => { };

    public ulong LocalSteamId => SteamUser.GetSteamID().m_SteamID;

    public void Start(Action<HSteamNetConnection> connecting, Action<HSteamNetConnection, string> closed)
    {
        _connecting = connecting;
        _closed = closed;

        // A poll group lets every connection be drained with one call.
        _pollGroup = SteamNetworkingSockets.CreatePollGroup();
        _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
        _statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
    }

    private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t info)
    {
        switch (info.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                _connecting(info.m_hConn);
                return;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                _closed(info.m_hConn, info.m_info.m_szEndDebug);
                SteamNetworkingSockets.CloseConnection(info.m_hConn, 0, null, false);
                return;
        }
    }

    public void Accept(HSteamNetConnection connection)
    {
        SteamNetworkingSockets.AcceptConnection(connection);
        SteamNetworkingSockets.SetConnectionPollGroup(connection, _pollGroup);
    }

    public void Close(HSteamNetConnection connection, string? reason)
        => SteamNetworkingSockets.CloseConnection(connection, 0, reason, false);

    public ulong RemoteSteamId(HSteamNetConnection connection)
        => SteamNetworkingSockets.GetConnectionInfo(connection, out var info)
            ? info.m_identityRemote.GetSteamID64()
            : 0UL;

    public bool Send(HSteamNetConnection connection, byte[] message, bool reliable)
    {
        var buffer = Marshal.AllocHGlobal(message.Length);

        try
        {
            Marshal.Copy(message, 0, buffer, message.Length);

            int flags = reliable
                ? Constants.k_nSteamNetworkingSend_Reliable
                : Constants.k_nSteamNetworkingSend_Unreliable;

            SteamNetworkingSockets.SendMessageToConnection(connection, buffer, (uint)message.Length, flags, out _);

            return true;
        }
        catch
        {
            // A closing connection throws; the status callback handles cleanup.
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public int Receive(int max, Action<HSteamNetConnection, byte[]> handle)
    {
        int count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(
            _pollGroup, _messageBuffer, Math.Min(max, _messageBuffer.Length));

        for (var i = 0; i < count; i++)
        {
            try
            {
                var native = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_messageBuffer[i]);
                var bytes = new byte[native.m_cbSize];
                Marshal.Copy(native.m_pData, bytes, 0, native.m_cbSize);

                handle(native.m_conn, bytes);
            }
            catch (Exception)
            {
                // A message that cannot be read is skipped so the rest of the batch is still handled and released.
            }
            finally
            {
                SteamNetworkingMessage_t.Release(_messageBuffer[i]);
            }
        }

        return count;
    }

    public void Dispose()
    {
        _statusCallback?.Dispose();

        if (_pollGroup.m_HSteamNetPollGroup != 0)
        {
            SteamNetworkingSockets.DestroyPollGroup(_pollGroup);
        }

        if (_listenSocket.m_HSteamListenSocket != 0)
        {
            SteamNetworkingSockets.CloseListenSocket(_listenSocket);
        }
    }
}
