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
    public const byte TagPlayerRepDamage = 64;
    public const byte TagPlayerRepTeleport = 69;

    /// <summary>Bytes of avatar proportions that precede the barcode.</summary>
    private const int AvatarStatsSize = FusionProtocol.AvatarStatFloatCount * 4;

    /// <summary>
    /// Steps over tag, relay type, channel, the nullable sender byte and the payload
    /// length. Returns false when the message is too short to hold them.
    /// </summary>
    private static bool TrySkipPrefix(ref FusionNetReader reader, ReadOnlySpan<byte> message, byte expectedTag)
    {
        if (message.Length < 3 || message[0] != expectedTag)
        {
            return false;
        }

        reader.ReadByte();
        byte relayType = reader.ReadByte();
        reader.ReadByte();

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
}
