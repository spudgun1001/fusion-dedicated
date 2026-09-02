namespace FusionDedicated.Server;

public enum SteamInitResult
{
    Ok,
    RefusedByClient,
    NativeLibraryMissing,
}

/// <summary>
/// Wraps SteamAPI.Init, which throws when the native library is absent rather than
/// returning false. That is the most likely first-run failure in a fresh container.
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

    /// <summary>
    /// Retries until Steam is ready. A fresh install spends minutes updating itself,
    /// and SteamAPI_Init reports no running instance the whole time, so a single
    /// attempt turns a slow start into a crash. A missing native library is not
    /// retried: that never becomes true by waiting.
    /// </summary>
    public static SteamInitResult InitWithRetry(
        Func<bool> init, int attempts, Action<string> log, Action<int> wait)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var result = TryInit(init);

            if (result == SteamInitResult.Ok)
            {
                if (attempt > 1)
                {
                    log($"Steam answered on attempt {attempt}.");
                }

                return result;
            }

            if (result == SteamInitResult.NativeLibraryMissing)
            {
                return result;
            }

            if (attempt == attempts)
            {
                return result;
            }

            if (attempt == 1 || attempt % 6 == 0)
            {
                log($"Steam is not ready yet (attempt {attempt} of {attempts}); still waiting.");
            }

            wait(attempt);
        }

        return SteamInitResult.RefusedByClient;
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
