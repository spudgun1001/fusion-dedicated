using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Server;

/// <summary>Where a spawn put a crate's root, kept until the first pose after it.</summary>
public readonly record struct SpawnRoot(Vec3 Position, byte[] Rotation);

/// <summary>
/// How a crate's root sits against its first body, in that body's frame. A pose only carries
/// the first body, and a joiner's copy is spawned at the root.
/// </summary>
public readonly record struct RootOffset(Vec3 Position, Quat Rotation)
{
    /// <summary>How far the first body may be from the root when the offset is learned.</summary>
    public const float MaxDistance = 5f;

    /// <summary>How long after the spawn the first pose may arrive and still be trusted.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);

    /// <returns>Null when the pose is too far from the root, too late, or unreadable.</returns>
    public static RootOffset? Capture(SpawnRoot root, DateTime spawnedAt, DateTime now, Vec3 bodyPosition,
        byte[] bodyRotation)
    {
        if (now - spawnedAt > MaxAge || Rotations.TryDecode(bodyRotation) is not { } bodyTurn)
        {
            return null;
        }

        var toRoot = new Vec3(
            root.Position.X - bodyPosition.X,
            root.Position.Y - bodyPosition.Y,
            root.Position.Z - bodyPosition.Z);

        // Written this way round so a position that is not a number is refused too.
        if (!(toRoot.Magnitude <= MaxDistance))
        {
            return null;
        }

        // Any other length was sent to clients as upright.
        var rootTurn = Rotations.TryDecode(root.Rotation) ?? Quat.Identity;
        var undo = Rotations.Inverse(bodyTurn);
        var offset = new RootOffset(Rotations.Rotate(undo, toRoot), Rotations.Multiply(undo, rootTurn));

        return Finite(offset.Position) && Finite(offset.Rotation) ? offset : null;
    }

    /// <summary>Where the root is for a first body at this pose, with the rotation in the seven byte form.</summary>
    /// <returns>Null when the rotation is unreadable or the answer is not a number.</returns>
    public (Vec3 Position, byte[] Rotation)? Apply(Vec3 bodyPosition, byte[] bodyRotation)
    {
        if (Rotations.TryDecode(bodyRotation) is not { } bodyTurn)
        {
            return null;
        }

        var along = Rotations.Rotate(bodyTurn, Position);
        var position = new Vec3(bodyPosition.X + along.X, bodyPosition.Y + along.Y, bodyPosition.Z + along.Z);
        var rotation = Rotations.Multiply(bodyTurn, Rotation);

        return Finite(position) && Finite(rotation) ? (position, Rotations.Encode(rotation)) : null;
    }

    private static bool Finite(Vec3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool Finite(Quat q)
        => float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W);
}
