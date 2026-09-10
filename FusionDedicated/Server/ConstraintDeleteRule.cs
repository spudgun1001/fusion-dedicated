using FusionDedicated.Server.Safety;

namespace FusionDedicated.Server;

public enum ConstraintDeleteVerdict
{
    Clear,
    PassOn,
    NotAConstraint,
    NotYours,
}

/// <summary>
/// What to do with a client's delete for a constraint end. The id is two bytes an
/// untrusted client chose, so only a constraint end may be cleared from the books.
/// </summary>
public static class ConstraintDeleteRule
{
    public static ConstraintDeleteVerdict Decide(
        TrackedEntity? entity, byte sender, PermissionLevel senderRank, bool protectedServer)
    {
        // An end the server no longer has. Clients only act on an id that is a
        // constraint for them, so passing it on is what lets them drop it.
        if (entity is null)
        {
            return ConstraintDeleteVerdict.PassOn;
        }

        if (!entity.Synthetic)
        {
            return ConstraintDeleteVerdict.NotAConstraint;
        }

        if (protectedServer && !DespawnAuthority.MayDespawn(entity.OwnerSmallId, sender, senderRank))
        {
            return ConstraintDeleteVerdict.NotYours;
        }

        return ConstraintDeleteVerdict.Clear;
    }
}
