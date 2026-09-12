using Steamworks;

namespace FusionDedicated.Server;

/// <summary>Every socket call the server makes, so tests can stand in for Steam.</summary>
public interface ISocketTransport : IDisposable
{
    /// <summary>The server's own Steam ID, for the log.</summary>
    ulong LocalSteamId { get; }

    /// <summary>Opens the listen socket and reports connections arriving and closing.</summary>
    void Start(Action<HSteamNetConnection> connecting, Action<HSteamNetConnection, string> closed);

    void Accept(HSteamNetConnection connection);

    void Close(HSteamNetConnection connection, string? reason);

    /// <summary>The Steam ID on the other end, or zero when it is not known.</summary>
    ulong RemoteSteamId(HSteamNetConnection connection);

    /// <returns>Whether the message actually went out.</returns>
    bool Send(HSteamNetConnection connection, byte[] message, bool reliable);

    /// <summary>Hands up to <paramref name="max"/> waiting messages to the handler.</summary>
    /// <returns>How many there were.</returns>
    int Receive(int max, Action<HSteamNetConnection, byte[]> handle);
}
