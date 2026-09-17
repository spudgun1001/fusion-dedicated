using System.Runtime.InteropServices;
using Steamworks;

namespace FusionDedicated.Server;

/// <summary>The real transport: Steam's networking sockets over the relay.</summary>
public sealed class SteamSocketTransport : ISocketTransport
{
    private IntPtr[] _messageBuffer = new IntPtr[128];

    private HSteamListenSocket _listenSocket;
    private HSteamNetPollGroup _pollGroup;
    private Callback<SteamNetConnectionStatusChangedCallback_t>? _statusCallback;

    private Action<HSteamNetConnection> _connecting = _ => { };
    private Action<HSteamNetConnection, string> _closed = (_, _) => { };
    private readonly Action<string>? _onReadFailure;
    private readonly ReceiveFailureLog _receiveFailures = new();

    public SteamSocketTransport(Action<string>? onReadFailure = null)
    {
        _onReadFailure = onReadFailure;
    }

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

    public string? LastSendFailure { get; private set; }

    public unsafe bool Send(HSteamNetConnection connection, byte[] message, bool reliable)
    {
        int flags = reliable
            ? Constants.k_nSteamNetworkingSend_Reliable
            : Constants.k_nSteamNetworkingSend_Unreliable;

        try
        {
            // Pinned only for the call, since Steam copies the bytes before it returns.
            fixed (byte* data = message)
            {
                var result = SteamNetworkingSockets.SendMessageToConnection(
                    connection, (IntPtr)data, (uint)message.Length, flags, out _);

                if (result != EResult.k_EResultOK)
                {
                    LastSendFailure = result.ToString();
                    return false;
                }
            }

            return true;
        }
        catch (Exception e)
        {
            // A closing connection throws; the status callback handles cleanup.
            LastSendFailure = e.Message;
            return false;
        }
    }

    public ConnectionHealth? Health(HSteamNetConnection connection)
    {
        var status = new SteamNetConnectionRealTimeStatus_t();
        var lanes = new SteamNetConnectionRealTimeLaneStatus_t();

        try
        {
            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) != EResult.k_EResultOK)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        return new ConnectionHealth(status.m_nPing, status.m_flConnectionQualityLocal, status.m_flConnectionQualityRemote,
            status.m_flOutBytesPerSec, status.m_cbPendingUnreliable + status.m_cbPendingReliable + status.m_cbSentUnackedReliable,
            (long)status.m_usecQueueTime);
    }

    public int Receive(int max, Action<HSteamNetConnection, byte[]> handle)
    {
        if (max > _messageBuffer.Length)
        {
            _messageBuffer = new IntPtr[max];
        }

        int count = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_pollGroup, _messageBuffer, max);

        // A negative count is Steam failing, not an empty queue, and would otherwise look like nothing to read.
        if (count < 0)
        {
            if (_receiveFailures.Failed(count, DateTime.UtcNow) is { } line)
            {
                _onReadFailure?.Invoke(line);
            }

            return 0;
        }

        for (var i = 0; i < count; i++)
        {
            try
            {
                var native = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_messageBuffer[i]);
                var bytes = new byte[native.m_cbSize];
                Marshal.Copy(native.m_pData, bytes, 0, native.m_cbSize);

                handle(native.m_conn, bytes);
            }
            catch (Exception e)
            {
                // A message that cannot be read is skipped so the rest of the batch is still handled and released.
                _onReadFailure?.Invoke(e.Message);
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
