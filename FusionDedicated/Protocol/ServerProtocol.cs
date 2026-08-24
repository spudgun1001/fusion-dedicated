using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Protocol;

/// <summary>
/// The half of the wire format a host needs: reading what clients send and writing
/// the replies. The client half lives in the shared FusionProtocol.
/// </summary>
public static class ServerProtocol
{
    private const byte RelayTypeNone = 0;
    private const byte ChannelReliable = 0;

    private const byte RelayTypeToClients = 2;

    public const byte TagSceneLoad = 12;
    public const byte TagDynamicsAssignment = 201;
    public const byte TagServerSettings = 45;
    public const byte TagDespawnRequest = 22;
    public const byte TagDespawnResponse = 23;
    public const byte TagPlayerMetadataResponse = 60;
    public const byte TagPermissionCommandRequest = 68;
    public const byte TagModInfoRequest = 77;
    public const byte TagModInfoResponse = 78;

    public sealed record ConnectionRequest(
        ulong PlatformId,
        Version Version,
        string AvatarBarcode,
        byte[] AvatarStats,
        Dictionary<string, string> Metadata,
        List<string> EquippedItems);

    /// <summary>
    /// Parses a client's ConnectionRequest. Layout mirrors ConnectionRequestData:
    /// ulong, Version(3 ints), barcode, 420 bytes of avatar stats, metadata, equipped.
    /// </summary>
    public static ConnectionRequest? TryReadConnectionRequest(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (reader.ReadByte() != FusionProtocol.TagConnectionRequest)
            {
                return null;
            }

            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            if (relayType != RelayTypeNone)
            {
                reader.ReadNullableByte();
            }

            reader.ReadInt32(); // payload length

            ulong platformId = reader.ReadUInt64();

            int major = reader.ReadInt32();
            int minor = reader.ReadInt32();
            int build = reader.ReadInt32();

            string barcode = reader.ReadString() ?? "";

            var stats = reader.ReadRaw(FusionProtocol.AvatarStatsSize).ToArray();

            var metadata = new Dictionary<string, string>();
            int metaCount = reader.ReadInt32();

            for (var i = 0; i < metaCount; i++)
            {
                string key = reader.ReadString() ?? "";
                string value = reader.ReadString() ?? "";
                metadata[key] = value;
            }

            var equipped = new List<string>();
            int equippedCount = reader.ReadInt32();

            for (var i = 0; i < equippedCount; i++)
            {
                equipped.Add(reader.ReadString() ?? "");
            }

            return new ConnectionRequest(platformId, new Version(major, minor, build),
                barcode, stats, metadata, equipped);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// ConnectionResponse: tells a client about a player, either themselves on join
    /// or an existing player during catchup.
    /// PlayerID(ulong + byte + metadata + equipped) + barcode + stats + isInitialJoin.
    /// </summary>
    public static byte[] WriteConnectionResponse(ulong platformId, byte smallId,
        Dictionary<string, string> metadata, List<string> equippedItems,
        string avatarBarcode, byte[] avatarStats, bool isInitialJoin)
    {
        var payload = new FusionNetWriter(1024);

        // PlayerID
        payload.Write(platformId);
        payload.Write(smallId);
        payload.Write(metadata);
        payload.Write(equippedItems);

        payload.Write(avatarBarcode);

        if (avatarStats.Length == FusionProtocol.AvatarStatsSize)
        {
            payload.WriteRaw(avatarStats);
        }
        else
        {
            // Neutral proportions rather than zeros — zeroed limbs divide by zero
            // when the receiver builds a rig.
            for (var i = 0; i < FusionProtocol.AvatarStatFloatCount; i++)
            {
                payload.Write(1f);
            }
        }

        payload.Write(isInitialJoin);

        return WrapNone(FusionProtocol.TagConnectionResponse, payload.ToArray());
    }

    /// <summary>
    /// SceneLoad: instructs a client which level to load. Sent with RelayType.None
    /// straight down the client's own connection.
    /// </summary>
    public static byte[] WriteSceneLoad(string levelBarcode, string loadingScreenBarcode)
    {
        var payload = new FusionNetWriter(256);

        payload.Write(levelBarcode);
        payload.Write(loadingScreenBarcode);

        return WrapNone(TagSceneLoad, payload.ToArray());
    }

    /// <summary>
    /// DynamicsAssignment carries gamemode metadata dictionaries. A relay-only server
    /// has no gamemodes running, so both maps go out empty.
    /// </summary>
    public static byte[] WriteEmptyDynamicsAssignment()
    {
        var payload = new FusionNetWriter(32);

        payload.Write(0); // gamemode metadatas
        payload.Write(0); // second map

        return WrapNone(TagDynamicsAssignment, payload.ToArray());
    }

    /// <summary>
    /// Disconnect with a reason — used to reject a join or kick someone.
    /// </summary>
    public static byte[] WriteDisconnect(ulong platformId, string reason)
    {
        var payload = new FusionNetWriter(256);

        payload.Write(platformId);
        payload.Write(reason);

        return WrapNone(FusionProtocol.TagDisconnect, payload.ToArray());
    }

    /// <summary>
    /// ServerSettings: hands a client the current LobbyInfo. Fusion's client stores it
    /// straight into LobbyInfoManager, so this is what makes a permission or gameplay
    /// change take effect for players who are already connected.
    /// </summary>
    public static byte[] WriteServerSettings(string lobbyInfoJson)
    {
        var payload = new FusionNetWriter(lobbyInfoJson.Length + 64);

        payload.Write(lobbyInfoJson);

        return WrapNone(TagServerSettings, payload.ToArray());
    }

    /// <summary>
    /// PlayerMetadataResponse: overwrites one metadata key on one player for everybody.
    /// Used to change someone's PermissionLevel without making them reconnect.
    /// </summary>
    public static byte[] WritePlayerMetadataResponse(byte targetSmallId, string key, string value)
    {
        var payload = new FusionNetWriter(256);

        payload.Write(targetSmallId); // PlayerReference
        payload.Write(key);
        payload.Write(value);

        var message = new FusionNetWriter(payload.Position + 32);

        message.Write(TagPlayerMetadataResponse);
        message.Write(RelayTypeToClients);
        message.Write(ChannelReliable);
        message.WriteNullable(null); // Sender — the server itself has no small ID
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// A client asking for an entity to be removed. DespawnRequest is server-only —
    /// clients never act on it directly — so nothing despawns unless the server
    /// answers with a DespawnResponse of its own.
    /// </summary>
    public static (ushort EntityId, bool DespawnEffect)? TryReadDespawnRequest(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            reader.ReadByte(); // tag
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            if (relayType != RelayTypeNone)
            {
                reader.ReadNullableByte();
            }

            reader.ReadInt32(); // payload length

            return (reader.ReadUInt16(), reader.ReadBool());
        }
        catch
        {
            return null;
        }
    }

    public static byte[] WriteDespawnResponse(byte despawnerSmallId, ushort entityId, bool despawnEffect)
    {
        var payload = new FusionNetWriter(16);

        payload.Write(despawnerSmallId); // PlayerReference
        payload.WriteUInt16(entityId);
        payload.Write(despawnEffect);

        var message = new FusionNetWriter(payload.Position + 32);

        message.Write(TagDespawnResponse);
        message.Write(RelayTypeToClients);
        message.Write(ChannelReliable);
        message.WriteNullable(despawnerSmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// A client that is missing the current level asks the host which mod.io mod it
    /// comes from. On a dedicated server there is no game install to look the answer
    /// up in, so the operator supplies the ids and this hands them over — which is
    /// what lets a modded map download itself on join.
    /// </summary>
    public static (byte? Target, string Barcode, uint TrackerId)? TryReadModInfoRequest(
        ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            reader.ReadByte(); // tag
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            byte? target = null;

            if (relayType == 4)
            {
                target = reader.ReadNullableByte();
            }

            if (relayType != RelayTypeNone)
            {
                reader.ReadNullableByte(); // sender
            }

            reader.ReadInt32(); // payload length

            return (target, reader.ReadString() ?? "", reader.ReadUInt32());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a ModInfoResponse so the server can learn the mod.io ids its players
    /// already know. The response carries no barcode — only the tracker id the
    /// request went out with — so the caller has to remember which barcode that was.
    /// </summary>
    public static (byte? Target, int ModId, int? ModFileId, uint TrackerId)? TryReadModInfoResponse(
        ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            reader.ReadByte(); // tag
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            byte? target = null;

            if (relayType == 4)
            {
                target = reader.ReadNullableByte();
            }

            if (relayType != RelayTypeNone)
            {
                reader.ReadNullableByte(); // sender
            }

            reader.ReadInt32(); // payload length

            int modId = reader.ReadInt32();
            int? fileId = reader.ReadBool() ? reader.ReadInt32() : null;

            reader.ReadBool();   // HasFile
            reader.ReadString(); // Platform

            return (target, modId, fileId, reader.ReadUInt32());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Rewrites a ToTarget message's recipient. Used to hand a mod info request that
    /// was addressed to the server over to a player who actually owns the mod; their
    /// reply is already addressed back to the original asker.
    /// </summary>
    public static byte[]? RetargetToTarget(byte[] message, byte newTarget)
    {
        // Prefix: tag, relayType, channel, target(nullable), ...
        if (message.Length < 5 || message[1] != 4 || message[3] == 0)
        {
            return null;
        }

        var copy = (byte[])message.Clone();
        copy[4] = newTarget;

        return copy;
    }

    /// <summary>
    /// ModInfoResponse. Layout is SerializedModIOFile — mod id, nullable file id,
    /// a has-file flag and the platform the files were built for — then the tracker
    /// id the request came in with.
    /// </summary>
    public static byte[] WriteModInfoResponse(byte targetSmallId, int modId, int? modFileId,
        string platform, uint trackerId)
    {
        var payload = new FusionNetWriter(128);

        payload.Write(modId);

        payload.Write(modFileId.HasValue);

        if (modFileId.HasValue)
        {
            payload.Write(modFileId.Value);
        }

        payload.Write(modId > 0);   // HasFile
        payload.Write(platform);
        payload.WriteUInt32(trackerId);

        var message = new FusionNetWriter(payload.Position + 32);

        message.Write(TagModInfoResponse);
        message.Write((byte)4);          // ToTarget
        message.Write(ChannelReliable);
        message.WriteNullable(targetSmallId);
        message.WriteNullable(null);     // Sender — the relay has no small ID
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    public enum PermissionCommand : byte
    {
        Unknown = 0,
        Kick = 1,
        Ban = 2,
        TeleportToThem = 3,
        TeleportToMe = 4,
    }

    /// <summary>
    /// A moderation command sent by a player through the in-game menu. The server is
    /// the only thing that can carry it out, so it also decides whether they may.
    /// </summary>
    public static (PermissionCommand Command, byte? Target)? TryReadPermissionCommand(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            reader.ReadByte(); // tag
            byte relayType = reader.ReadByte();
            reader.ReadByte(); // channel

            if (relayType != RelayTypeNone)
            {
                reader.ReadNullableByte();
            }

            reader.ReadInt32(); // payload length

            var command = (PermissionCommand)reader.ReadByte();

            return (command, reader.ReadNullableByte());
        }
        catch
        {
            return null;
        }
    }

    private static byte[] WrapNone(byte tag, byte[] payload)
    {
        var message = new FusionNetWriter(payload.Length + 32);

        message.Write(tag);
        message.Write(RelayTypeNone);
        message.Write(ChannelReliable);

        // RelayType.None carries no Sender byte.

        message.WriteBlock(payload);

        return message.ToArray();
    }

    /// <summary>
    /// Rewrites a relayed message's Sender field so downstream clients see who it
    /// came from. Returns the original bytes when the route carries no sender.
    /// </summary>
    public static byte[] StampSender(byte[] message, byte senderSmallId)
    {
        if (message.Length < 4 || message[1] == RelayTypeNone)
        {
            return message;
        }

        var stamped = (byte[])message.Clone();

        // Prefix: tag, relayType, channel, [target...], sender
        int offset = 3;
        byte relayType = message[1];

        if (relayType == 4) // ToTarget: nullable byte target
        {
            offset += message[offset] != 0 ? 2 : 1;
        }
        else if (relayType == 5) // ToTargets: int length + bytes
        {
            int count = (message[offset] << 24) | (message[offset + 1] << 16)
                      | (message[offset + 2] << 8) | message[offset + 3];

            offset += 4 + count;
        }

        if (offset + 1 < stamped.Length)
        {
            stamped[offset] = 1;                 // Sender.HasValue
            stamped[offset + 1] = senderSmallId; // Sender
        }

        return stamped;
    }

    /// <summary>
    /// Reads a relayed message's route so the server knows where to forward it.
    /// </summary>
    public static (byte RelayType, byte Channel, byte? Target) ReadRoute(ReadOnlySpan<byte> message)
    {
        if (message.Length < 3)
        {
            return (0, 0, null);
        }

        byte relayType = message[1];
        byte channel = message[2];
        byte? target = null;

        if (relayType == 4 && message.Length > 4 && message[3] != 0)
        {
            target = message[4];
        }

        return (relayType, channel, target);
    }
}
