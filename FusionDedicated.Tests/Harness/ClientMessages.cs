using System.Buffers.Binary;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Harness;

/// <summary>Client messages the server has no builder for, framed the way LabFusion 1.14.1 sends them.</summary>
public static class ClientMessages
{
    private const byte ToServer = 1;
    private const byte ToOtherClients = 3;
    private const byte Reliable = 0;

    public static byte[] Join(ServerConfig config, ulong platformId, string name, byte[]? avatarStats = null)
        => FusionProtocol.BuildConnectionRequest(
            platformId, new Version(config.VersionMajor, config.VersionMinor, 0),
            "SLZ.BONELAB.Content.Avatar.FordBW",
            new Dictionary<string, string> { ["Username"] = name }, new List<string>(), avatarStats);

    public static byte[] Metadata(byte player, string key, string value) => MetadataFor(player, player, key, value);

    /// <summary>A metadata request naming somebody other than the sender.</summary>
    public static byte[] MetadataFor(byte sender, byte target, string key, string value)
    {
        var payload = new FusionNetWriter(64);
        payload.Write(target);
        payload.Write(key);
        payload.Write(value);

        return Wrap(GateProtocol.TagPlayerMetadataRequest, ToServer, sender, payload.ToArray());
    }

    public static byte[] PermissionCommand(byte player, ServerProtocol.PermissionCommand command, byte target)
    {
        var payload = new FusionNetWriter(8);
        payload.Write((byte)command);
        payload.WriteNullable(target);

        return Wrap(ServerProtocol.TagPermissionCommandRequest, ToServer, player, payload.ToArray());
    }

    public static byte[] FinishedLoading(byte player) => Metadata(player, "Loading", "false");

    public static byte[] Despawn(byte player, ushort entity)
    {
        var payload = new FusionNetWriter(8);
        payload.WriteUInt16(entity);
        payload.Write(false);

        return Wrap(ServerProtocol.TagDespawnRequest, ToServer, player, payload.ToArray());
    }

    public static byte[] CullStatus(byte player, ushort entity, bool culled)
    {
        var payload = new FusionNetWriter(8);
        payload.WriteUInt16(entity);
        payload.Write(culled);

        return Wrap(FusionProtocol.TagEntityCullStatus, ToOtherClients, player, payload.ToArray());
    }

    public static byte[] SlotInsert(byte player, ushort slot, ushort weapon, byte index)
        => Module(player, ModuleProtocol.InventorySlotInsertTag, ModuleProtocol.WriteInventorySlotInsert(slot, weapon, index), ToOtherClients);

    public static byte[] SlotDrop(byte player, ushort slot, byte grabber, byte index, byte hand)
    {
        var payload = new FusionNetWriter(8);
        payload.WriteUInt16(slot);
        payload.Write(grabber);
        payload.Write(index);
        payload.Write(hand);

        return Module(player, ModuleProtocol.InventorySlotDropTag, payload.ToArray(), ToOtherClients);
    }

    /// <summary>A PlayerRepDamage hit sent to one player: the damage float first, then the rest of the attack.</summary>
    public static byte[] Damage(byte attacker, byte target, float damage)
    {
        var payload = new byte[45];
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(0, 4), damage);

        var message = new FusionNetWriter(64);
        message.Write(GateProtocol.TagPlayerRepDamage);
        message.Write((byte)4);     // ToTarget
        message.Write(Reliable);
        message.WriteNullable(target);
        message.WriteNullable(attacker);
        message.WriteBlock(payload);

        return message.ToArray();
    }

    /// <summary>A plausible avatar's proportions: every float 1, and masses adding up to 80.</summary>
    public static byte[] AvatarStats()
    {
        var stats = new byte[FusionProtocol.AvatarStatsSize];

        for (var i = 0; i < FusionProtocol.AvatarStatFloatCount; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(stats.AsSpan(i * 4, 4), 1f);
        }

        SetAvatarStat(stats, "massArm", 5f);
        SetAvatarStat(stats, "massChest", 25f);
        SetAvatarStat(stats, "massHead", 6f);
        SetAvatarStat(stats, "massLeg", 12f);
        SetAvatarStat(stats, "massPelvis", 15f);
        SetAvatarStat(stats, "massTotal", 80f);

        return stats;
    }

    public static void SetAvatarStat(byte[] stats, string field, float value)
    {
        int index = AvatarStatsCheck.FieldNames.ToList().IndexOf(field);

        if (index < 0)
        {
            throw new ArgumentException($"No avatar stat called {field}", nameof(field));
        }

        BinaryPrimitives.WriteSingleBigEndian(stats.AsSpan(index * 4, 4), value);
    }

    /// <summary>A PlayerRepAvatar message: the proportions block, then the barcode. Sent to one player when a target is given.</summary>
    public static byte[] Avatar(byte player, string barcode, byte[] stats, byte? target = null)
    {
        var payload = new FusionNetWriter(stats.Length + 64);
        payload.WriteRaw(stats);
        payload.Write(barcode);

        if (target is not { } victim)
        {
            return Wrap(GateProtocol.TagPlayerRepAvatar, ToOtherClients, player, payload.ToArray());
        }

        var message = new FusionNetWriter(stats.Length + 96);
        message.Write(GateProtocol.TagPlayerRepAvatar);
        message.Write((byte)4);     // ToTarget
        message.Write(Reliable);
        message.WriteNullable(victim);
        message.WriteNullable(player);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    private static byte[] Module(byte player, long handler, byte[] handlerPayload, byte relayType)
    {
        var body = new FusionNetWriter(handlerPayload.Length + ModuleProtocol.HandlerTagBytes);

        Span<byte> tag = stackalloc byte[ModuleProtocol.HandlerTagBytes];
        BinaryPrimitives.WriteInt64BigEndian(tag, handler);

        body.WriteRaw(tag);
        body.WriteRaw(handlerPayload);

        return Wrap(ModuleProtocol.TagModule, relayType, player, body.ToArray());
    }

    private static byte[] Wrap(byte tag, byte relayType, byte sender, byte[] payload)
    {
        var message = new FusionNetWriter(payload.Length + 32);
        message.Write(tag);
        message.Write(relayType);
        message.Write(Reliable);
        message.WriteNullable(sender);
        message.WriteBlock(payload);

        return message.ToArray();
    }
}
