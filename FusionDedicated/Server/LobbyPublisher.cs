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

    // One per attempt. A single shared slot meant a callback resolved whichever
    // attempt happened to be current rather than its own, so an abandoned attempt
    // that answered late completed the next one's task and left a second live
    // lobby in the browser that nothing afterwards wrote to or closed.
    private readonly List<CallResult<LobbyCreated_t>> _outstanding = new();
    private readonly object _outstandingLock = new();

    private readonly CallResult<LobbyMatchList_t> _browserCheck = CallResult<LobbyMatchList_t>.Create();
    private readonly CallResult<LobbyMatchList_t> _codeCheck = CallResult<LobbyMatchList_t>.Create();

    private CSteamID _lobbyId = CSteamID.Nil;
    private long _generation;

    public bool IsPublished => _lobbyId != CSteamID.Nil;
    public ulong LobbyId => _lobbyId.m_SteamID;

    /// <summary>
    /// Creates the lobby. Steamworks.NET reports the result through a CallResult, so
    /// this bridges it onto a Task, the caller must keep pumping SteamAPI callbacks
    /// while awaiting, or it will never complete.
    /// </summary>
    public Task<bool> PublishAsync(int maxPlayers)
    {
        var pending = new TaskCompletionSource<bool>();
        var handle = CallResult<LobbyCreated_t>.Create();

        long generation = Interlocked.Increment(ref _generation);

        lock (_outstandingLock)
        {
            _outstanding.Add(handle);
        }

        handle.Set(
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxPlayers),
            (result, failure) =>
            {
                lock (_outstandingLock)
                {
                    _outstanding.Remove(handle);
                }

                handle.Dispose();

                if (failure || result.m_eResult != EResult.k_EResultOK)
                {
                    pending.TrySetResult(false);
                    return;
                }

                var lobby = new CSteamID(result.m_ulSteamIDLobby);

                // Answered after somebody gave up on it. Steam made the lobby
                // anyway, so it is left rather than abandoned, or it would sit in
                // the browser as an entry nobody can join.
                if (Interlocked.Read(ref _generation) != generation)
                {
                    try { SteamMatchmaking.LeaveLobby(lobby); } catch { }

                    pending.TrySetResult(false);
                    return;
                }

                _lobbyId = lobby;
                SteamMatchmaking.SetLobbyJoinable(lobby, true);

                pending.TrySetResult(true);
            });

        return pending.Task;
    }

    /// <summary>
    /// Rewrites the lobby metadata. Called whenever the roster or settings change so
    /// the browser shows live player counts.
    ///
    /// Also how a lost lobby is noticed. Steam hands back false when the lobby is
    /// no longer ours to write to, which is what happens if the Steam connection
    /// drops and comes back: the lobby is gone, but nothing tells us, and the id
    /// we are holding stays as valid-looking as it ever was. A server ran 22
    /// hours and quietly vanished from the browser that way, with the retry that
    /// exists for this never firing because it only asks whether we think we are
    /// published.
    /// </summary>
    /// <returns>False when the lobby has gone and needs publishing again.</returns>
    public bool Update(ServerConfig config, IReadOnlyList<ConnectedPlayer> players, ulong hostSteamId)
    {
        if (!IsPublished)
        {
            return false;
        }

        bool full = players.Count >= config.MaxPlayers;

        var info = LobbyInfoBuilder.Build(config, players, hostSteamId);

        if (!SetLobbyDataChecked(IdentifierKey, bool.TrueString))
        {
            // A failed write is not proof the lobby has gone: it is also what a
            // transient fault looks like. Left rather than merely forgotten, so a
            // lobby that does still exist does not stay in the browser as an
            // entry nobody can join once the replacement is published.
            try { SteamMatchmaking.LeaveLobby(_lobbyId); } catch { }

            _lobbyId = CSteamID.Nil;
            return false;
        }

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

        return true;
    }

    /// <summary>
    /// Runs the search Fusion's browser runs, then a search by our code, and reports
    /// how many lobbies the browser search listed and whether ours was in each. Steam
    /// answers through RunCallbacks, so this never waits. A new check replaces one
    /// still waiting, and a failed search reports nothing.
    /// </summary>
    public void CheckVisibility(ServerConfig config, Action<int, bool, bool> done)
    {
        if (!IsPublished)
        {
            return;
        }

        var lobby = _lobbyId;

        // No result count filter, the same as Fusion, so Steam lists at most 50 by distance.
        AddFusionFilters();
        SteamMatchmaking.AddRequestLobbyListStringFilter(FullKey, bool.FalseString, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListNumericalFilter(VersionMajorKey, config.VersionMajor, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListNumericalFilter(VersionMinorKey, config.VersionMinor, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListNumericalFilter(PrivacyKey, 1, ELobbyComparison.k_ELobbyComparisonNotEqual);
        SteamMatchmaking.AddRequestLobbyListNumericalFilter(PrivacyKey, 3, ELobbyComparison.k_ELobbyComparisonNotEqual);

        _browserCheck.Set(SteamMatchmaking.RequestLobbyList(), (browser, failed) =>
        {
            if (failed)
            {
                return;
            }

            int listed = (int)browser.m_nLobbiesMatching;
            bool inBrowser = Contains(lobby, listed);

            AddFusionFilters();
            SteamMatchmaking.AddRequestLobbyListStringFilter(LobbyCodeKey, config.ServerCode.ToUpperInvariant(), ELobbyComparison.k_ELobbyComparisonEqual);

            _codeCheck.Set(SteamMatchmaking.RequestLobbyList(), (byCode, codeFailed) =>
            {
                if (!codeFailed)
                {
                    done(listed, inBrowser, Contains(lobby, (int)byCode.m_nLobbiesMatching));
                }
            });
        });
    }

    private static void AddFusionFilters()
    {
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListStringFilter(IdentifierKey, bool.TrueString, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(HasLobbyOpenKey, bool.TrueString, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameName, ELobbyComparison.k_ELobbyComparisonEqual);
    }

    private static bool Contains(CSteamID lobby, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (SteamMatchmaking.GetLobbyByIndex(i) == lobby)
            {
                return true;
            }
        }

        return false;
    }

    private bool SetLobbyDataChecked(string key, string value)
        => SteamMatchmaking.SetLobbyData(_lobbyId, key, value);

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
        _browserCheck.Dispose();
        _codeCheck.Dispose();

        // Anything still waiting on Steam, so no registration outlives us.
        lock (_outstandingLock)
        {
            foreach (var handle in _outstanding)
            {
                try { handle.Dispose(); } catch { }
            }

            _outstanding.Clear();
        }
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
