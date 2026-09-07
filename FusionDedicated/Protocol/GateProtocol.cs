using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Protocol;

/// <summary>
/// The message types the extended gates inspect. Tags and field layouts were taken
/// from LabFusion 1.14.1 by decompiling NativeMessageTag and the matching Data
/// classes, so they are read rather than guessed.
/// </summary>
public static class GateProtocol
{
    public const byte TagPlayerRepAvatar = 5;
    public const byte TagSlowMoButton = 58;
    public const byte TagPlayerMetadataRequest = 59;
    public const byte TagPlayerMetadataResponse = 60;
    public const byte TagPlayerRepDamage = 64;
    public const byte TagPlayerRepTeleport = 69;

    /// <summary>Bytes of avatar proportions that precede the barcode.</summary>
    private const int AvatarStatsSize = FusionProtocol.AvatarStatFloatCount * 4;

    /// <summary>
    /// Steps over tag, relay type, channel, the nullable sender byte and the payload
    /// length. Returns false when the message is too short to hold them.
    /// </summary>
    internal static bool TrySkipPrefix(ref FusionNetReader reader, ReadOnlySpan<byte> message, byte expectedTag)
    {
        if (message.Length < 3 || message[0] != expectedTag)
        {
            return false;
        }

        reader.ReadByte();
        byte relayType = reader.ReadByte();
        reader.ReadByte();

        // ToTarget carries the target before the sender. Skipping only the sender
        // left the reader two bytes short, which put route bytes inside the body
        // and, for an RPC variable, inside the key naming which variable it is.
        if (relayType == 4)
        {
            reader.ReadNullableByte();
        }

        if (relayType != 0)
        {
            reader.ReadNullableByte();
        }

        reader.ReadInt32();

        return true;
    }

    /// <summary>
    /// Damage dealt by a remote attack. SerializedAttack puts the float first, so it
    /// sits at the very front of the payload.
    /// </summary>
    public static float? TryReadDamage(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (!TrySkipPrefix(ref reader, message, TagPlayerRepDamage))
            {
                return null;
            }

            return reader.ReadSingle();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The avatar a client is switching to, read past its proportions block.</summary>
    public static string? TryReadAvatarBarcode(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (!TrySkipPrefix(ref reader, message, TagPlayerRepAvatar))
            {
                return null;
            }

            reader.ReadRaw(AvatarStatsSize);

            return reader.ReadString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The avatar's measurements, which travel in the same message as its barcode.
    ///
    /// A newcomer is told the stats every existing player joined with, and those
    /// were never updated on a swap. The model was right and the proportions were
    /// the previous avatar's, so height, reach and where a player could grab from
    /// were all somebody else's.
    /// </summary>
    public const byte TagPointItemEquipState = 206;

    /// <summary>
    /// The RPC variable tags. A level's own state lives in these: lights, gates,
    /// elevators, whatever an SDK map wires up.
    /// </summary>
    public static readonly byte[] RpcVariableTags = { 210, 211, 212, 213, 214 };

    /// <summary>
    /// Which variable an RPC message is about, as raw bytes.
    ///
    /// ComponentPathData names it the same way on every machine: whether it
    /// belongs to an entity, which one, which component, and a hash of where it
    /// sits in the level. Six bytes, or fourteen when the hash is there. The
    /// value follows and is not read, because replaying only needs the last
    /// message for each variable, whatever type it held.
    /// </summary>
    /// <summary>The body of a native message, past the prefix.</summary>
    public static byte[]? TryReadBody(ReadOnlySpan<byte> message, byte tag)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (!TrySkipPrefix(ref reader, message, tag))
            {
                return null;
            }

            return reader.ReadRaw(message.Length - reader.Position).ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Rebuilds an RPC variable message for one player, from the body we kept.
    /// </summary>
    public static byte[] BuildRpcVariable(byte tag, byte targetSmallId, byte[] body)
    {
        var message = new FusionNetWriter(body.Length + 32);

        message.Write(tag);
        message.Write((byte)4);                 // ToTarget
        message.Write((byte)0);                 // Reliable
        message.WriteNullable(targetSmallId);
        message.WriteNullable((byte)0);         // sender: the server
        message.WriteBlock(body);

        return message.ToArray();
    }

    public static byte[]? TryReadRpcPath(ReadOnlySpan<byte> payload)
    {
        try
        {
            if (payload.Length < 6)
            {
                return null;
            }

            // HasEntity, EntityID, ComponentIndex, then whether a hash follows.
            int length = payload[5] != 0 ? 14 : 6;

            return payload.Length < length ? null : payload[..length].ToArray();
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// A cosmetic being put on or taken off.
    ///
    /// Every client keeps this on its copy of the player, so a host's catch-up
    /// carries the live list. Ours was written once at the handshake, so anything
    /// equipped or removed since showed wrong to whoever joined next.
    /// </summary>
    public static (string Barcode, bool Equipped)? TryReadEquipState(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (!TrySkipPrefix(ref reader, message, TagPointItemEquipState))
            {
                return null;
            }

            string? barcode = reader.ReadString();

            return barcode == null ? null : (barcode, reader.ReadBool());
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? TryReadAvatarStats(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            return TrySkipPrefix(ref reader, message, TagPlayerRepAvatar)
                ? reader.ReadRaw(AvatarStatsSize).ToArray()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
