using System.Buffers.Binary;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Client messages the server has no builder for, framed the way LabFusion 1.14.1 sends them.</summary>
public static class ClientMessages
{
    private const byte ToServer = 1;
    private const byte ToClients = 2;
    private const byte ToOtherClients = 3;
    private const byte Reliable = 0;

    public static byte[] Join(ServerConfig config, ulong platformId, string name)
        => FusionProtocol.BuildConnectionRequest(
            platformId, new Version(config.VersionMajor, config.VersionMinor, 0),
            "SLZ.BONELAB.Content.Avatar.FordBW",
            new Dictionary<string, string> { ["Username"] = name }, new List<string>());

    public static byte[] Metadata(byte player, string key, string value)
    {
        var payload = new FusionNetWriter(64);
        payload.Write(player);
        payload.Write(key);
        payload.Write(value);

        return Wrap(GateProtocol.TagPlayerMetadataRequest, ToServer, player, payload.ToArray());
    }

    public static byte[] FinishedLoading(byte player) => Metadata(player, "Loading", "False");

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

    public static byte[] Module(byte player, long handler, byte[] handlerPayload)
        => Module(player, handler, handlerPayload, ToClients);

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
