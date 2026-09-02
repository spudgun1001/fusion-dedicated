using System.Text.Json;

namespace FusionDedicated.Server;

/// <summary>
/// Builds the LobbyInfo blob Fusion clients read.
///
/// The same object goes to two places: into the Steam lobby's metadata, where the
/// server browser reads it, and into a ServerSettings message, which is how a live
/// client learns the rules it has to obey. Building it once keeps the two in step -
/// a setting changed in the panel takes effect for players already in the server
/// rather than only for the next person to look at the browser.
///
/// Enum-valued fields are written as plain numbers because Fusion's LobbyInfo
/// deserialises them with default System.Text.Json settings.
/// </summary>
public static class LobbyInfoBuilder
{
    public static object Build(ServerConfig config, IReadOnlyList<ConnectedPlayer> players, ulong hostSteamId)
    {
        return new
        {
            lobbyID = hostSteamId,
            lobbyCode = config.ServerCode,
            lobbyName = config.ServerName,
            lobbyDescription = config.Description,
            lobbyVersion = config.Version,
            lobbyHostName = config.ServerName,

            playerCount = players.Count,
            playerList = new
            {
                players = players.Select(p => new
                {
                    platformID = p.PlatformId,
                    username = p.Username,
                    nickname = p.Nickname,
                    description = "",
                    permissionLevel = (int)p.Permission,
                    avatarTitle = p.AvatarBarcode,
                    avatarModID = -1,
                }).ToArray(),
            },

            levelTitle = config.LevelTitle,
            levelBarcode = config.LevelBarcode,
            levelModID = -1,

            gamemodeTitle = "",
            gamemodeBarcode = "",
            timeBetweenGamemodeRounds = config.TimeBetweenGamemodeRounds,

            nameTags = config.NameTags,
            privacy = config.Privacy,
            slowMoMode = config.SlowMoMode,
            maxPlayers = config.MaxPlayers,
            voiceChat = config.VoiceChat,
            playerConstraining = config.PlayerConstraining,
            mortality = config.Mortality,
            friendlyFire = config.FriendlyFire,
            knockout = config.Knockout,
            knockoutLength = config.KnockoutLength,
            maxAvatarHeight = config.MaxAvatarHeight,

            devTools = (int)config.DevTools,
            constrainer = (int)config.Constrainer,
            customAvatars = (int)config.CustomAvatars,
            kicking = (int)config.Kicking,
            banning = (int)config.Banning,
            teleportation = (int)config.Teleportation,
        };
    }

    public static string Serialize(ServerConfig config, IReadOnlyList<ConnectedPlayer> players, ulong hostSteamId)
        => JsonSerializer.Serialize(Build(config, players, hostSteamId));
}
