using System.Text.Json;
using Steamworks;

namespace FusionDedicated.Server;

/// <summary>
/// Publishes the Steam lobby that makes this server visible in the in-game browser.
///
/// Fusion finds servers by querying Steam's lobby list for a specific set of metadata
/// keys, so a dedicated server just has to write the same keys. Nothing is spoofed:
/// Fusion itself runs under SteamVR's app id rather than BONELAB's, and so does this.
/// </summary>
public sealed class LobbyPublisher : IDisposable
{
    private const string IdentifierKey = "MarrowFusion";
    private const string HasLobbyOpenKey = "HasLobbyOpen";
    private const string KeyCollectionKey = "KeyCollection";
    private const string LobbyCodeKey = "LobbyCode";
    private const string PrivacyKey = "Privacy";
    private const string FullKey = "Full";
    private const string VersionMajorKey = "VersionMajor";
    private const string VersionMinorKey = "VersionMinor";
    private const string GameKey = "Game";
    private const string LobbyInfoKey = "LobbyInfo";

    private const string GameName = "BONELAB";

    private CallResult<LobbyCreated_t>? _createResult;
    private CSteamID _lobbyId = CSteamID.Nil;
    private TaskCompletionSource<bool>? _pending;

    public bool IsPublished => _lobbyId != CSteamID.Nil;
    public ulong LobbyId => _lobbyId.m_SteamID;

    /// <summary>
    /// Creates the lobby. Steamworks.NET reports the result through a CallResult, so
    /// this bridges it onto a Task — the caller must keep pumping SteamAPI callbacks
    /// while awaiting, or it will never complete.
    /// </summary>
    public Task<bool> PublishAsync(int maxPlayers)
    {
        _pending = new TaskCompletionSource<bool>();

        _createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);

        var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxPlayers);
        _createResult.Set(call);

        return _pending.Task;
    }

    private void OnLobbyCreated(LobbyCreated_t result, bool failure)
    {
        if (failure || result.m_eResult != EResult.k_EResultOK)
        {
            _pending?.TrySetResult(false);
            return;
        }

        _lobbyId = new CSteamID(result.m_ulSteamIDLobby);

        SteamMatchmaking.SetLobbyJoinable(_lobbyId, true);

        _pending?.TrySetResult(true);
    }

    /// <summary>
    /// Rewrites the lobby metadata. Called whenever the roster or settings change so
    /// the browser shows live player counts.
    /// </summary>
    public void Update(ServerConfig config, IReadOnlyList<ConnectedPlayer> players, ulong hostSteamId)
    {
        if (!IsPublished)
        {
            return;
        }

        bool full = players.Count >= config.MaxPlayers;

        var info = LobbyInfoBuilder.Build(config, players, hostSteamId);

        SteamMatchmaking.SetLobbyData(_lobbyId, IdentifierKey, bool.TrueString);
        SteamMatchmaking.SetLobbyData(_lobbyId, HasLobbyOpenKey, bool.TrueString);
        SteamMatchmaking.SetLobbyData(_lobbyId, LobbyCodeKey, config.ServerCode.ToUpperInvariant());
        SteamMatchmaking.SetLobbyData(_lobbyId, PrivacyKey, config.Privacy.ToString());
        SteamMatchmaking.SetLobbyData(_lobbyId, FullKey, full.ToString());
        SteamMatchmaking.SetLobbyData(_lobbyId, VersionMajorKey, config.VersionMajor.ToString());
        SteamMatchmaking.SetLobbyData(_lobbyId, VersionMinorKey, config.VersionMinor.ToString());
        SteamMatchmaking.SetLobbyData(_lobbyId, GameKey, GameName);
        SteamMatchmaking.SetLobbyData(_lobbyId, LobbyInfoKey, JsonSerializer.Serialize(info));

        var keys = new[]
        {
            IdentifierKey, HasLobbyOpenKey, LobbyCodeKey, PrivacyKey, FullKey,
            VersionMajorKey, VersionMinorKey, GameKey, LobbyInfoKey,
        };

        SteamMatchmaking.SetLobbyData(_lobbyId, KeyCollectionKey, JsonSerializer.Serialize(keys));
    }

    public void Close()
    {
        if (IsPublished)
        {
            SteamMatchmaking.SetLobbyData(_lobbyId, HasLobbyOpenKey, bool.FalseString);
            SteamMatchmaking.LeaveLobby(_lobbyId);
            _lobbyId = CSteamID.Nil;
        }
    }

    public void Dispose()
    {
        _createResult?.Dispose();
    }

    public static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[8];

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        }

        return new string(chars);
    }
}
