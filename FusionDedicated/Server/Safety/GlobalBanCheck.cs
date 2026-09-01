namespace FusionDedicated.Server.Safety;

/// <summary>
/// Looks a joining player up in Fusion's community ban list. It never refuses a join:
/// a third-party list should not decide who may play here.
/// </summary>
public static class GlobalBanCheck
{
    public static GlobalBanEntry? Find(GlobalBanList? list, ulong platformId)
    {
        if (list is null)
        {
            return null;
        }

        foreach (var ban in list.Bans)
        {
            foreach (var platform in ban.Platforms)
            {
                if (platform.PlatformId == platformId)
                {
                    return ban;
                }
            }
        }

        return null;
    }
}
