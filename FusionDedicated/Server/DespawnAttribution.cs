namespace FusionDedicated.Server;

/// <summary>
/// Picks whose name goes on a despawn. Clients ignore a despawn from a sender they
/// cannot resolve, so it has to be a real player, and a player's own client does not
/// act on one credited to them, so it must not be the player receiving it.
/// </summary>
public static class DespawnAttribution
{
    public static byte For(byte recipient, IReadOnlyList<byte> present)
    {
        foreach (byte candidate in present)
        {
            if (candidate != recipient)
            {
                return candidate;
            }
        }

        // Alone in the server, so there is nobody else to credit it to.
        return recipient;
    }
}
