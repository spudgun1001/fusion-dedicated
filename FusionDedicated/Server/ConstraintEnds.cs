using System.Buffers.Binary;

namespace FusionDedicated.Server;

public enum ConstraintEndKind : byte
{
    Null = 0,
    Entity = 1,
    Scene = 2,
}

/// <summary>What one end of a constraint is attached to.</summary>
public readonly record struct ConstraintEnd(ConstraintEndKind Kind, ushort EntityId)
{
    /// <summary>A player's rig: Fusion registers each rig under its player's SmallID. Id 0 is the server's stand-in, which has none.</summary>
    public bool IsPlayer => Kind == ConstraintEndKind.Entity && EntityId is >= 1 and <= byte.MaxValue;

    public bool IsProp => Kind == ConstraintEndKind.Entity && EntityId >= EntityRegistry.FirstEntityId;
}

/// <summary>
/// Reads the two ends of Fusion's ConstraintCreateMessage: SmallID, a nullable
/// ConstrainerID, Mode, then each end as a type byte followed by an entity id and
/// body index, or a scene path. Big-endian, as Fusion writes it.
/// </summary>
public static class ConstraintEnds
{
    /// <returns>Both ends, or null when the payload will not read.</returns>
    public static (ConstraintEnd First, ConstraintEnd Second)? TryRead(ReadOnlySpan<byte> payload)
    {
        int at = 0;

        if (!Skip(payload, ref at, 1) || !TryByte(payload, ref at, out byte hasConstrainer))
        {
            return null;
        }

        if (hasConstrainer != 0 && !Skip(payload, ref at, 2))
        {
            return null;
        }

        if (!Skip(payload, ref at, 1))
        {
            return null;
        }

        if (TryEnd(payload, ref at) is not { } first || TryEnd(payload, ref at) is not { } second)
        {
            return null;
        }

        return (first, second);
    }

    private static ConstraintEnd? TryEnd(ReadOnlySpan<byte> payload, ref int at)
    {
        if (!TryByte(payload, ref at, out byte kind))
        {
            return null;
        }

        switch (kind)
        {
            case (byte)ConstraintEndKind.Null:
                return new ConstraintEnd(ConstraintEndKind.Null, 0);

            case (byte)ConstraintEndKind.Entity:
            {
                if (payload.Length - at < 4)
                {
                    return null;
                }

                ushort id = BinaryPrimitives.ReadUInt16BigEndian(payload[at..]);
                at += 4;

                return new ConstraintEnd(ConstraintEndKind.Entity, id);
            }

            case (byte)ConstraintEndKind.Scene:
            {
                if (payload.Length - at < 4)
                {
                    return null;
                }

                int length = BinaryPrimitives.ReadInt32BigEndian(payload[at..]);
                at += 4;

                if (length < -1 || (length > 0 && payload.Length - at < length))
                {
                    return null;
                }

                at += Math.Max(length, 0);

                return new ConstraintEnd(ConstraintEndKind.Scene, 0);
            }

            default:
                return null;
        }
    }

    private static bool TryByte(ReadOnlySpan<byte> payload, ref int at, out byte value)
    {
        if (at >= payload.Length)
        {
            value = 0;
            return false;
        }

        value = payload[at++];
        return true;
    }

    private static bool Skip(ReadOnlySpan<byte> payload, ref int at, int count)
    {
        if (payload.Length - at < count)
        {
            return false;
        }

        at += count;
        return true;
    }
}
