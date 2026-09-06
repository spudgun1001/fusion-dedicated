namespace FusionDedicated.Server;

/// <summary>
/// What a player who has just arrived needs to be told about the world.
///
/// Fusion sends creation catch-up from the host and nowhere else, and no client
/// on a dedicated server is the host, so nobody sends it. Until this existed, a
/// player who joined a running server saw an empty level: every gun, prop and
/// crate spawned before they arrived was invisible to them, and so were the guns
/// in everyone's hands.
/// </summary>
public static class WorldCatchup
{
    /// <summary>
    /// The entities to replay, in the order they were spawned.
    ///
    /// Discovered entities are left out: those were part of the level when it
    /// loaded, so the newcomer's own copy of the scene already has them, and
    /// sending them would make a second one appear.
    /// </summary>
    public static List<TrackedEntity> For(IEnumerable<TrackedEntity> entities)
        => entities
            .Where(e => !e.Discovered && !string.IsNullOrWhiteSpace(e.Barcode))
            .OrderBy(e => e.Id)
            .ToList();
}
