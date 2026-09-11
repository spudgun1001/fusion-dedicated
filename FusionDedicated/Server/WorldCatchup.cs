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
    ///
    /// So are the synthetic ones. The two ends of a constraint are tracked under
    /// a barcode we invented, and telling a client to spawn it would be naming
    /// something no pallet has. The constraint itself is put back by its own
    /// message, not by a spawn.
    /// </summary>
    public static List<TrackedEntity> For(IEnumerable<TrackedEntity> entities)
        => entities
            .Where(e => !e.Discovered
                && !e.Synthetic
                && !string.IsNullOrWhiteSpace(e.Barcode))
            .OrderBy(e => e.Id)
            .ToList();

    /// <summary>
    /// Who a newcomer is told owns a scene prop. They ask that player for its
    /// state, so a stale name leaves a held gun floating in the air for them.
    /// </summary>
    /// <param name="anyoneElse">Somebody present who is not the newcomer, when both owners have gone.</param>
    public static byte PropOwner(byte? current, byte cached, byte newcomer, Func<byte, bool> present,
        byte? anyoneElse = null)
    {
        if (current is { } owner && owner != newcomer && present(owner))
        {
            return owner;
        }

        if (cached != newcomer && present(cached))
        {
            return cached;
        }

        return anyoneElse ?? newcomer;
    }

    /// <summary>
    /// Whether a holstered weapon goes back on the hip for a newcomer. Not while
    /// somebody holds it: the draw was missed and the slot is really empty.
    /// </summary>
    public static bool ShouldReseat(IReadOnlyCollection<byte> holders) => holders.Count == 0;

    /// <summary>
    /// Where a client's request for an entity's state should really go. It asks the
    /// owner it was told about, who may have left or handed the entity on while it
    /// loaded, or player 0, who does not exist here.
    /// </summary>
    /// <returns>The owner to send it to instead, or null to relay it unchanged.</returns>
    public static byte? DataRequestTarget(byte? asked, byte? current, byte requester, Func<byte, bool> present)
        => current is { } owner && owner != requester && owner != asked && present(owner)
            ? owner
            : null;

    /// <summary>Whether a metadata change is a player saying they have finished loading.</summary>
    public static bool FinishedLoading(string key, string value)
        => string.Equals(key, "Loading", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(value, out bool loading)
            && !loading;

    /// <summary>
    /// Whether a seat message is passed on.
    ///
    /// Fusion answers a data request by sending each occupied seat to the asker,
    /// stamped as coming from whoever answered, so the asker seats the answerer in
    /// the rider's place. When the server has that seat it replays the right rider,
    /// so the broken copy is dropped. Anything else goes through as before.
    /// </summary>
    public static bool PassSeatMessage(byte relayType, bool ingress, bool seatRecorded)
        => !(relayType == 4 && ingress && seatRecorded);

    /// <summary>Whether a relay type is a live seat message rather than a catch-up reply.</summary>
    public static bool IsLiveSeat(byte relayType) => relayType is 2 or 3;

    /// <summary>Whether a seat is kept: an ingress, live, into an entity the server knows.</summary>
    public static bool KeepSeat(byte relayType, bool ingress, bool known)
        => ingress && known && IsLiveSeat(relayType);

    /// <summary>
    /// The seats to tell a client about when it asks about a vehicle: everyone
    /// sitting in it who is still here, except the client itself, in sit order.
    /// </summary>
    public static IReadOnlyList<SeatRecord> SeatsToReplay(IEnumerable<SeatRecord> records, ushort entityId,
        byte requester, Func<byte, bool> present)
        => records
            .Where(r => r.EntityId == entityId && r.Rider != requester && present(r.Rider))
            .ToList();

    /// <summary>
    /// Whether a pose shows a vehicle has a new owner: the sender sits in it and
    /// the server still has somebody else down as the owner. Sitting in a driver seat
    /// makes the driver the owner on every client that saw the seat without sending
    /// anything, so the server and any client that missed it keep the old owner and
    /// throw the driver's poses away.
    /// </summary>
    public static bool OwnerFromSeatedPose(byte sender, byte? registryOwner, ushort? senderSeatEntity,
        ushort poseEntity)
        => senderSeatEntity == poseEntity && registryOwner != sender;

    /// <summary>
    /// Who takes over an entity when its owner leaves: somebody sitting in it,
    /// then somebody holding it, then the fallback. Never the player who left.
    /// </summary>
    public static byte? HeirFor(IReadOnlyList<byte> riders, IReadOnlyList<byte> holders, byte? fallback, byte departed)
    {
        foreach (byte rider in riders)
        {
            if (rider != departed)
            {
                return rider;
            }
        }

        foreach (byte holder in holders)
        {
            if (holder != departed)
            {
                return holder;
            }
        }

        return fallback == departed ? null : fallback;
    }
}
