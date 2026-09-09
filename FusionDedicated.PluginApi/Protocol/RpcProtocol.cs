using System.Buffers.Binary;
using System.Text;

namespace BonelabServerBrowser.Fusion;

/// <summary>Which of Fusion's RPC components a message belongs to.</summary>
public enum RpcKind : byte
{
    /// <summary>Not one of them.</summary>
    None = 0,

    /// <summary>A one-shot, with no value. Tag 209.</summary>
    Event = 209,

    Int = 210,
    Float = 211,
    Bool = 212,
    String = 213,
    Vector3 = 214,
}

/// <summary>
/// Fusion's RPC components, as they travel.
///
/// These are the parts of the Marrow SDK a pallet can carry without any code of
/// its own: an author drops an RPCInt or an RPCEvent on a prefab in Unity, wires
/// it with UltEvents, and it networks. That makes them the only way a mod.io
/// pallet and a server plugin can talk to each other, since a pallet cannot ship
/// a MelonLoader assembly and the server cannot run one.
///
/// Every one carries a ComponentPathData first, which is how a component is named
/// on the wire: an entity and an index for a spawned prop, or a hash of its place
/// in the level for one that came with the map. Neither is readable, but both are
/// stable, so the server treats a path as an opaque key.
/// </summary>
public static class RpcProtocol
{
    /// <summary>ComponentPathData without the optional hash: entity flag, id, index, no hash.</summary>
    public const int ShortPathBytes = 6;

    /// <summary>The same with the hash: two more ints.</summary>
    public const int LongPathBytes = 14;

    public static bool IsRpc(byte tag) => tag is >= 209 and <= 214;

    public static RpcKind KindOf(byte tag) => IsRpc(tag) ? (RpcKind)tag : RpcKind.None;

    /// <summary>What a payload says about the component it names.</summary>
    /// <param name="Path">
    /// The path bytes, exactly as they arrived. Opaque, and the key everything
    /// else uses, so it has to be repeated rather than rebuilt.
    /// </param>
    /// <param name="EntityId">Zero when the component came with the level.</param>
    public readonly record struct RpcPath(byte[] Path, bool HasEntity, ushort EntityId, ushort ComponentIndex)
    {
        public string Key => Convert.ToHexString(Path);
    }

    /// <summary>
    /// The path of one component on a spawned entity, built rather than observed.
    ///
    /// This is what makes a prop addressable from a single sighting: a plugin sees
    /// one RPC from a phone, takes its entity, and can then talk to every other
    /// component on that phone by index without waiting for each to speak.
    ///
    /// Only for components on an entity. One that came with the level is named by
    /// a hash of where it sits, which cannot be worked out from here.
    /// </summary>
    public static string PathFor(ushort entityId, ushort componentIndex)
    {
        Span<byte> path = stackalloc byte[ShortPathBytes];

        path[0] = 1;    // HasEntity
        BinaryPrimitives.WriteUInt16BigEndian(path[1..], entityId);
        BinaryPrimitives.WriteUInt16BigEndian(path[3..], componentIndex);
        path[5] = 0;    // no hash follows

        return Convert.ToHexString(path);
    }

    /// <summary>Reads the component a payload names, or null when it names none.</summary>
    public static RpcPath? TryReadPath(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < ShortPathBytes)
        {
            return null;
        }

        int length = payload[5] != 0 ? LongPathBytes : ShortPathBytes;

        if (payload.Length < length)
        {
            return null;
        }

        return new RpcPath(
            payload[..length].ToArray(),
            payload[0] != 0,
            BinaryPrimitives.ReadUInt16BigEndian(payload[1..]),
            BinaryPrimitives.ReadUInt16BigEndian(payload[3..]));
    }

    /// <summary>
    /// The value after the path, as whichever type the tag says.
    ///
    /// An event carries none, so it always reads as nothing.
    /// </summary>
    public static RpcValue ReadValue(RpcKind kind, ReadOnlySpan<byte> payload)
    {
        if (TryReadPath(payload) is not { } path)
        {
            return RpcValue.Nothing;
        }

        var body = payload[path.Path.Length..];

        try
        {
            return kind switch
            {
                RpcKind.Int => RpcValue.OfInt(BinaryPrimitives.ReadInt32BigEndian(body)),
                RpcKind.Float => RpcValue.OfFloat(ReadFloat(body)),
                RpcKind.Bool => RpcValue.OfBool(body[0] != 0),
                RpcKind.String => RpcValue.OfString(ReadString(body)),
                RpcKind.Vector3 => RpcValue.OfVector(
                    ReadFloat(body), ReadFloat(body[4..]), ReadFloat(body[8..])),
                _ => RpcValue.Nothing,
            };
        }
        catch
        {
            return RpcValue.Nothing;
        }
    }

    /// <summary>Builds the payload for a value on a component, path first.</summary>
    public static byte[] WriteValue(RpcKind kind, byte[] path, RpcValue value)
    {
        var writer = new FusionNetWriter(path.Length + 64);

        writer.WriteRaw(path);

        switch (kind)
        {
            case RpcKind.Int:
                writer.Write(value.Int);
                break;

            case RpcKind.Float:
                writer.Write(value.Float);
                break;

            case RpcKind.Bool:
                writer.Write(value.Bool);
                break;

            case RpcKind.String:
                writer.Write(value.Text ?? "");
                break;

            case RpcKind.Vector3:
                writer.Write(value.X);
                writer.Write(value.Y);
                writer.Write(value.Z);
                break;
        }

        return writer.ToArray();
    }

    private static float ReadFloat(ReadOnlySpan<byte> at)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(at));

    private static string ReadString(ReadOnlySpan<byte> at)
    {
        int length = BinaryPrimitives.ReadInt32BigEndian(at);

        return length < 0 || length > at.Length - 4
            ? ""
            : Encoding.UTF8.GetString(at.Slice(4, length));
    }
}

/// <summary>
/// Whatever an RPC component carries. One shape for all of them, because a plugin
/// is handed values of every kind through one handler and a hierarchy would only
/// make that harder to read.
/// </summary>
public readonly record struct RpcValue(
    RpcKind Kind, int Int, float Float, bool Bool, string Text, float X, float Y, float Z)
{
    public static readonly RpcValue Nothing = new(RpcKind.None, 0, 0f, false, "", 0f, 0f, 0f);

    public static RpcValue OfInt(int value) => Nothing with { Kind = RpcKind.Int, Int = value };

    public static RpcValue OfFloat(float value) => Nothing with { Kind = RpcKind.Float, Float = value };

    public static RpcValue OfBool(bool value) => Nothing with { Kind = RpcKind.Bool, Bool = value };

    public static RpcValue OfString(string value)
        => Nothing with { Kind = RpcKind.String, Text = value ?? "" };

    public static RpcValue OfVector(float x, float y, float z)
        => Nothing with { Kind = RpcKind.Vector3, X = x, Y = y, Z = z };
}
