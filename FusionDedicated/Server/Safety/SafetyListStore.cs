namespace FusionDedicated.Server.Safety;

/// <summary>
/// Holds Fusion's community lists and caches them on disk, so a server with no
/// outbound internet still starts.
/// </summary>
public sealed class SafetyListStore
{
    public const string RepositoryUrl =
        "https://raw.githubusercontent.com/Lakatrazz/Fusion-Lists/main/";

    private const string ModsFile = "globalModBlacklist.json";
    private const string BansFile = "globalBans.json";

    private readonly string _cacheDirectory;

    public SafetyListStore(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
    }

    public GlobalModBlacklist? Mods { get; private set; }

    public GlobalBanList? Bans { get; private set; }

    public void LoadCache()
    {
        Mods = SafetyListParser.ParseModBlacklist(ReadCache(ModsFile) ?? "") ?? Mods;
        Bans = SafetyListParser.ParseBanList(ReadCache(BansFile) ?? "") ?? Bans;
    }

    /// <summary>Fetches both lists. The downloader is injected, and returns null for any failure.</summary>
    public async Task RefreshAsync(Func<string, Task<string?>> download)
    {
        string? mods = await download(RepositoryUrl + ModsFile);
        var parsedMods = mods is null ? null : SafetyListParser.ParseModBlacklist(mods);

        if (parsedMods != null)
        {
            Mods = parsedMods;
            WriteCache(ModsFile, mods!);
        }

        string? bans = await download(RepositoryUrl + BansFile);
        var parsedBans = bans is null ? null : SafetyListParser.ParseBanList(bans);

        if (parsedBans != null)
        {
            Bans = parsedBans;
            WriteCache(BansFile, bans!);
        }
    }

    private string? ReadCache(string name)
    {
        try
        {
            string path = Path.Combine(_cacheDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private void WriteCache(string name, string contents)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllText(Path.Combine(_cacheDirectory, name), contents);
        }
        catch
        {
            // A read-only volume costs a refetch next start, nothing more.
        }
    }
}
