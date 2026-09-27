using System.Runtime.InteropServices;
using Steamworks;

namespace FusionDedicated;

/// <summary>Steam's per-connection send rate and buffer, set once for every connection at startup.</summary>
public static class SteamSendLimits
{
    /// <summary>The values to set, leaving out any of zero or less so Steam keeps its own.</summary>
    public static IReadOnlyList<(ESteamNetworkingConfigValue, int)> From(ServerConfig config)
        => new[]
            {
                (ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, config.SendRateMin),
                (ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, config.SendRateMax),
                (ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, config.SendBufferSize),
            }
            .Where(limit => limit.Item2 > 0).ToList();

    public static string Describe(ServerConfig config)
        => $"Steam send limits: rate {Kb(config.SendRateMin)}-{Kb(config.SendRateMax)} KB/s, buffer {Kb(config.SendBufferSize)} KB";

    private static string Kb(int bytes) => bytes > 0 ? (bytes / 1024).ToString() : "default";

    /// <summary>Sets them globally, so every connection made afterwards has them, and says what was set.</summary>
    public static string Apply(ServerConfig config)
    {
        string line = Describe(config);
        IntPtr value = Marshal.AllocHGlobal(sizeof(int));

        try
        {
            foreach (var (key, bytes) in From(config))
            {
                Marshal.WriteInt32(value, bytes);

                if (!SteamNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, value))
                {
                    line += $", Steam refused {key} = {bytes}";
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(value);
        }

        return line;
    }
}
