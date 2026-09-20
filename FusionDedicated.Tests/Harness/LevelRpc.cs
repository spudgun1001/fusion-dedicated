using System.Globalization;
using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// RPC components that came with the level, as a client puts them on the wire.
///
/// A level component is named by a hash of where it sits rather than by an entity,
/// and the index it carries lives in the last four bytes of that hash. Everything
/// the doors and phones plugins read off a level path is built from those bytes,
/// so a test that wants to be the level has to write them itself.
/// </summary>
public static class LevelRpc
{
    /// <summary>The 20 hex characters in front of the index: no entity, then the hash flag and the first hash int.</summary>
    public static string Hash(uint id) => "000000000001" + id.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>One component of a level object, at the index the prefab numbers it with.</summary>
    public static string Path(string hash, ushort index)
        => hash + ((uint)index).ToString("X8", CultureInfo.InvariantCulture);

    public static byte[] Int(byte sender, string hash, ushort index, int value)
        => Message(sender, RpcKind.Int, hash, index, RpcValue.OfInt(value));

    public static byte[] Bool(byte sender, string hash, ushort index, bool value)
        => Message(sender, RpcKind.Bool, hash, index, RpcValue.OfBool(value));

    public static byte[] Event(byte sender, string hash, ushort index)
        => Message(sender, RpcKind.Event, hash, index, RpcValue.Nothing);

    /// <summary>An RPC as a client sends it: ToOtherClients, reliable, sender stamped.</summary>
    public static byte[] Message(byte sender, RpcKind kind, string hash, ushort index, RpcValue value)
    {
        byte[] payload = RpcProtocol.WriteValue(kind, Convert.FromHexString(Path(hash, index)), value);
        var message = new FusionNetWriter(payload.Length + 32);

        message.Write((byte)kind);
        message.Write((byte)3);     // ToOtherClients
        message.Write((byte)0);     // reliable
        message.WriteNullable(sender);
        message.WriteBlock(payload);

        return message.ToArray();
    }

    /// <summary>What a player was sent on one component, oldest first.</summary>
    public static List<RpcValue> Heard(World world, FakePlayer player, string hash, ushort index, RpcKind kind)
        => Heard(world, player, Path(hash, index), kind);

    public static List<RpcValue> Heard(World world, FakePlayer player, string path, RpcKind kind)
    {
        var heard = new List<RpcValue>();

        foreach (var (message, _) in world.Transport.SentTo(player.Connection))
        {
            if (message.Length == 0 || message[0] != (byte)kind
                || Envelope.Read(message) is not { } envelope
                || RpcProtocol.TryReadPath(envelope.Payload) is not { } read
                || !string.Equals(read.Key, path, StringComparison.Ordinal))
            {
                continue;
            }

            heard.Add(RpcProtocol.ReadValue(kind, envelope.Payload));
        }

        return heard;
    }
}
