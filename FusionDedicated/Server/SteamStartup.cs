namespace FusionDedicated.Server;

public enum SteamInitResult
{
    Ok,
    RefusedByClient,
    NativeLibraryMissing,
}

/// <summary>
/// Wraps SteamAPI.Init so a missing native library is a message rather than a
/// stack trace. It throws when the library is absent instead of returning false,
/// which is the most likely first-run failure in a fresh container.
/// </summary>
public static class SteamStartup
{
    public static SteamInitResult TryInit(Func<bool> init)
    {
        try
        {
            return init() ? SteamInitResult.Ok : SteamInitResult.RefusedByClient;
        }
        catch (DllNotFoundException)
        {
            return SteamInitResult.NativeLibraryMissing;
        }
        catch (BadImageFormatException)
        {
            return SteamInitResult.NativeLibraryMissing;
        }
    }

    public static string Explain(SteamInitResult result) => result switch
    {
        SteamInitResult.NativeLibraryMissing =>
            "Steamworks could not load. libsteam_api.so is missing from the server "
            + "directory, or is built for the wrong architecture. Reinstall the "
            + "server, which fetches it from the Steamworks.NET release.",

        SteamInitResult.RefusedByClient =>
            "Steam refused to initialise. Check that the Steam client is running and "
            + "signed in, that the account owns SteamVR (app 250820), and that "
            + "steam_appid.txt sits next to the binary.",

        _ => "",
    };
}
