namespace BonelabServerBrowser.Fusion;

public static class FusionProtocol
{
    // Tags from LabFusion NativeMessageTag.
    public const byte TagConnectionRequest = 1;
    public const byte TagConnectionResponse = 2;
    public const byte TagDisconnect = 3;
    public const byte TagPlayerPoseUpdate = 4;

    private const byte RelayTypeToOtherClients = 3;
    private const byte ChannelUnreliable = 1;

    // RelayType.None, used before the client has a proper player ID assigned.
    private const byte RelayTypeNone = 0;

    // NetworkChannel.Reliable
    private const byte ChannelReliable = 0;

    /// <summary>
    /// Number of floats in SerializedAvatarStats: 73 plain floats + 8 soft ellipses of 4 floats.
    /// </summary>
    public const int AvatarStatFloatCount = 73 + (8 * 4);

    /// <summary>
    /// Builds a full ConnectionRequest packet, matching what LabFusion's
    /// ConnectionSender.SendConnectionRequest would put on the wire.
    /// </summary>
    public static byte[] BuildConnectionRequest(
        ulong platformId,
        Version version,
        string avatarBarcode,
        Dictionary<string, string> metadata,
        List<string> equippedItems,
        byte[]? avatarStats = null)
    {
        // Payload: the serialized ConnectionRequestData.
        var payload = new FusionNetWriter();

        payload.Write(platformId);

        payload.Write(version.Major);
        payload.Write(version.Minor);
        payload.Write(Math.Max(version.Build, 0));

        payload.Write(avatarBarcode);

        if (avatarStats != null && avatarStats.Length == AvatarStatsSize)
        {
            // Real proportions captured from a live player, see TryReadConnectionResponse.
            payload.WriteRaw(avatarStats);
        }
        else
        {
            WriteNeutralAvatarStats(payload);
        }

        payload.Write(metadata);
        payload.Write(equippedItems);

        // Envelope: MessagePrefix followed by the length-prefixed payload.
        var message = new FusionNetWriter();

        message.Write(TagConnectionRequest);
        message.Write(RelayTypeNone);
        message.Write(ChannelReliable);

        // Route.Type is None, so no Sender byte is written.

        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// Writes avatar proportions as all-ones rather than zeros.
    /// The host builds a player rig from these numbers, and zeroed proportions
    /// would divide by zero on their machine.
    /// </summary>
    private static void WriteNeutralAvatarStats(FusionNetWriter writer)
    {
        for (var i = 0; i < AvatarStatFloatCount; i++)
        {
            writer.Write(1f);
        }
    }

    /// <summary>
    /// Reads the reason string out of a Disconnect message, if that's what this is.
    /// Returns null for any other message type.
    /// </summary>
    public static string? TryReadDisconnectReason(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            byte tag = reader.ReadByte();

            if (tag != TagDisconnect)
            {
                return null;
            }

            ReadRouteAndSender(ref reader);

            reader.ReadInt32(); // payload length

            reader.ReadUInt64(); // target platform ID

            return reader.ReadString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a MessageRoute out of a prefix and returns the Sender.
    ///
    /// The shape depends on the relay type. ToTarget carries a nullable target byte
    /// and ToTargets a length-prefixed list, both BEFORE the sender. Assuming a fixed
    /// layout silently shifts every later field, which is what broke SpawnResponse
    /// parsing: real responses arrive as ToTarget, not ToClients.
    /// </summary>
    private static byte? ReadRouteAndSender(ref FusionNetReader reader)
    {
        byte relayType = reader.ReadByte();
        reader.ReadByte(); // channel

        switch (relayType)
        {
            case 4: // ToTarget
                reader.ReadNullableByte();
                break;

            case 5: // ToTargets
                int count = reader.ReadInt32();
                reader.ReadRaw(count);
                break;
        }

        // Sender is only present when the message is relayed at all.
        return relayType != RelayTypeNone ? reader.ReadNullableByte() : null;
    }

    public static byte? TryReadTag(ReadOnlySpan<byte> message)
    {
        return message.Length > 0 ? message[0] : null;
    }

    /// <summary>
    /// Number of bytes in a serialized SerializedAvatarStats block.
    /// </summary>
    public const int AvatarStatsSize = AvatarStatFloatCount * sizeof(float);

    /// <summary>
    /// Builds a PlayerPoseUpdate. Unlike the connection request this carries a real
    /// relay route, so the host knows which player the pose belongs to, it is
    /// dropped outright without our assigned SmallID.
    /// </summary>
    public static byte[] BuildPlayerPoseUpdate(byte senderSmallId, FusionRigPose pose)
    {
        var payload = new FusionNetWriter(FusionRigPose.Size);
        pose.Write(payload);

        var message = new FusionNetWriter();

        message.Write(TagPlayerPoseUpdate);
        message.Write(RelayTypeToOtherClients);
        message.Write(ChannelUnreliable);

        // Route.Type != None, so the prefix carries a nullable Sender.
        message.WriteNullable(senderSmallId);

        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    public const byte TagPlayerRepAction = 50;
    public const byte TagPlayerVoiceChat = 67;
    public const byte TagPlayerRepGrab = 9;
    public const byte TagPlayerRepRelease = 10;

    public enum Handedness : byte
    {
        UNDEFINED = 0,
        LEFT = 1,
        RIGHT = 2,
    }

    private const byte GrabGroupEntity = 1;

    /// <summary>
    /// Builds a PlayerRepGrab for a networked entity.
    ///
    /// Layout confirmed byte-for-byte against a capture of a real grab:
    ///   Handedness(1) GrabGroup(1) IsGrabbed(1) TargetInBase(19) index(2) id(2) = 26
    /// TargetInBase is the grip attach offset, position as 3 raw floats, then a
    /// SerializedQuaternion (3 shorts + a sign byte).
    /// </summary>
    public static byte[] BuildGrab(byte senderSmallId, Handedness hand, ushort gripIndex, ushort entityId,
        Vec3 targetPosition = default, Quat targetRotation = default)
    {
        var payload = new FusionNetWriter(64);

        payload.Write((byte)hand);
        payload.Write(GrabGroupEntity);
        payload.Write(true); // IsGrabbed. RequestGrab drops the message without it

        // SerializedTransform: uncompressed position, compressed rotation.
        payload.Write(targetPosition.X);
        payload.Write(targetPosition.Y);
        payload.Write(targetPosition.Z);
        WriteSerializedQuaternion(payload, targetRotation);

        payload.WriteUInt16(gripIndex);
        payload.WriteUInt16(entityId);

        return WrapReliableToOtherClients(TagPlayerRepGrab, senderSmallId, payload.ToArray());
    }

    /// <summary>
    /// Release carries nothing but the hand, confirmed against a 10 byte capture.
    /// </summary>
    public static byte[] BuildRelease(byte senderSmallId, Handedness hand)
    {
        var payload = new FusionNetWriter(4);
        payload.Write((byte)hand);

        return WrapReliableToOtherClients(TagPlayerRepRelease, senderSmallId, payload.ToArray());
    }

    private static byte[] WrapReliableToOtherClients(byte tag, byte senderSmallId, byte[] payload)
    {
        var message = new FusionNetWriter(payload.Length + 32);

        message.Write(tag);
        message.Write(RelayTypeToOtherClients);
        message.Write(ChannelReliable);
        message.WriteNullable(senderSmallId);
        message.WriteBlock(payload);

        return message.ToArray();
    }

    /// <summary>
    /// SerializedQuaternion: the three smallest components as shorts, plus a byte
    /// marking which component was dropped. See SerializedQuaternion.cs.
    /// </summary>
    private static void WriteSerializedQuaternion(FusionNetWriter writer, Quat q)
    {
        if (q == default)
        {
            q = Quat.Identity;
        }

        float[] components = { q.X, q.Y, q.Z, q.W };

        int largest = 0;

        for (var i = 1; i < 4; i++)
        {
            if (MathF.Abs(components[i]) > MathF.Abs(components[largest]))
            {
                largest = i;
            }
        }

        // Flip so the dropped component is positive and can be reconstructed.
        if (components[largest] < 0f)
        {
            for (var i = 0; i < 4; i++)
            {
                components[i] = -components[i];
            }
        }

        for (var i = 0; i < 4; i++)
        {
            if (i == largest)
            {
                continue;
            }

            // SerializedQuaternion.PRECISION_OFFSET, note this is 10000, unlike the
            // 30000 used by SerializedShortVector3.
            writer.WriteInt16((short)(components[i] * 10000f));
        }

        writer.Write((byte)largest);
    }


    private const byte RelayTypeToClients = 2;

    public enum PlayerActionType : byte
    {
        UNKNOWN = 0,
        JUMP = 1,
        DEATH = 2,
        DYING = 3,
        RECOVERY = 4,
        RESPAWN = 5,
    }

    /// <summary>
    /// Builds a PlayerRepAction. The host applies JUMP to the rig directly
    /// (remapHeptaRig.Jump()), so this is the only real "gesture" Fusion syncs.
    /// </summary>
    public static byte[] BuildPlayerAction(byte senderSmallId, PlayerActionType action, byte? otherPlayer = null)
    {
        var payload = new FusionNetWriter(8);

        payload.Write((byte)action);
        payload.WriteNullable(otherPlayer);

        var message = new FusionNetWriter();

        message.Write(TagPlayerRepAction);
        message.Write(RelayTypeToClients);
        message.Write(ChannelReliable);
        message.WriteNullable(senderSmallId);

        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// Reads an incoming PlayerPoseUpdate, returning the sender's SmallID and pose.
    /// </summary>
    public static (byte SmallId, FusionRigPose Pose)? TryReadPlayerPoseUpdate(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (reader.ReadByte() != TagPlayerPoseUpdate)
            {
                return null;
            }

            byte? sender = ReadRouteAndSender(ref reader);

            if (!sender.HasValue)
            {
                return null;
            }

            int payloadLength = reader.ReadInt32();

            if (payloadLength < FusionRigPose.Size)
            {
                return null;
            }

            return (sender.Value, FusionRigPose.Read(reader.ReadRaw(FusionRigPose.Size)));
        }
        catch
        {
            return null;
        }
    }

    public const byte TagSpawnRequest = 20;
    public const byte TagSpawnResponse = 21;

    /// <summary>
    /// Asks the host to spawn a spawnable. The host allocates the entity ID and
    /// answers with SpawnResponse, so this is how we learn what we created.
    ///
    /// Payload is SerializedSpawnData:
    ///   string Barcode, SerializedTransform(19), uint TrackerID, bool SpawnEffect,
    ///   EntitySource(1 byte)
    /// </summary>
    /// <param name="spawnEffect">Live clients send false; a real capture showed 0 here.</param>
    /// <param name="source">EntitySource. Captured traffic from a working client uses 1
    /// (Scene). The 3 previously guessed here produced no response at all.</param>
    public static byte[] BuildSpawnRequest(byte senderSmallId, string barcode, Vec3 position,
        uint trackerId, bool spawnEffect = false, byte source = 1)
    {
        var payload = new FusionNetWriter(128);

        payload.Write(barcode);

        payload.Write(position.X);
        payload.Write(position.Y);
        payload.Write(position.Z);
        WriteSerializedQuaternion(payload, Quat.Identity);

        payload.WriteUInt32(trackerId);
        payload.Write(spawnEffect);
        payload.Write(source);

        var message = new FusionNetWriter(160);

        message.Write(TagSpawnRequest);
        message.Write(RelayTypeToServer);
        message.Write(ChannelReliable);
        message.WriteNullable(senderSmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    public sealed record SpawnResponseInfo(byte OwnerId, ushort EntityId, string? Barcode, uint TrackerId);

    /// <summary>
    /// Writes a SpawnResponse exactly as LabFusion's SpawnResponseData would. Used to
    /// round-trip the layout offline; the host is the only real source of these.
    /// </summary>
    public static byte[] BuildSpawnResponse(byte senderSmallId, byte ownerId, ushort entityId,
        string barcode, Vec3 position, uint trackerId, bool spawnEffect = true, byte source = 3)
    {
        var payload = new FusionNetWriter(160);

        payload.Write(ownerId);
        payload.WriteUInt16(entityId);

        // SerializedSpawnData
        payload.Write(barcode);
        payload.Write(position.X);
        payload.Write(position.Y);
        payload.Write(position.Z);
        WriteSerializedQuaternion(payload, Quat.Identity);
        payload.WriteUInt32(trackerId);
        payload.Write(spawnEffect);
        payload.Write(source);

        var message = new FusionNetWriter(200);

        message.Write(TagSpawnResponse);
        message.Write(RelayTypeToClients);
        message.Write(ChannelReliable);
        message.WriteNullable(senderSmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// Reads a SpawnResponse. Also the way to learn valid barcodes: every spawn any
    /// player performs is broadcast with its barcode attached.
    /// </summary>
    /// <summary>
    /// Set to receive an explanation whenever a SpawnResponse fails to parse, with the
    /// raw bytes. Without this a bad layout just looks like "no spawns happened".
    /// </summary>
    public static Action<string>? OnParseFailure { get; set; }

    public static SpawnResponseInfo? TryReadSpawnResponse(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (reader.ReadByte() != TagSpawnResponse)
            {
                return null;
            }

            ReadRouteAndSender(ref reader);

            reader.ReadInt32(); // payload length

            byte ownerId = reader.ReadByte();
            ushort entityId = reader.ReadUInt16();

            string? barcode = reader.ReadString();

            // Skip the transform: 3 floats + SerializedQuaternion (3 shorts + byte).
            reader.ReadRaw(12 + 7);

            uint trackerId = reader.ReadUInt32();

            return new SpawnResponseInfo(ownerId, entityId, barcode, trackerId);
        }
        catch (Exception ex)
        {
            OnParseFailure?.Invoke(
                $"SpawnResponse не разобрался ({ex.GetType().Name}: {ex.Message})\n" +
                $"       {message.Length} байт: {Convert.ToHexString(message[..Math.Min(message.Length, 256)])}");

            return null;
        }
    }

    public const byte TagEntityPoseUpdate = 17;
    public const byte TagEntityOwnershipRequest = 15;
    public const byte TagEntityOwnershipResponse = 16;

    private const byte RelayTypeToServer = 1;

    /// <summary>
    /// Claims ownership of a world entity. The host only accepts EntityPoseUpdate from
    /// an entity's owner, so this must succeed before we can carry or throw anything.
    /// Payload is EntityPlayerData: byte PlayerID + ushort EntityID.
    /// </summary>
    public static byte[] BuildOwnershipRequest(byte senderSmallId, ushort entityId)
    {
        var payload = new FusionNetWriter(8);

        payload.Write(senderSmallId);
        payload.WriteUInt16(entityId);

        var message = new FusionNetWriter(32);

        message.Write(TagEntityOwnershipRequest);
        message.Write(RelayTypeToServer);
        message.Write(ChannelReliable);
        message.WriteNullable(senderSmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// Hands an entity to another player. The host echoes back whatever PlayerID the
    /// request names, so passing the host's ID gives physics authority back to it.
    /// </summary>
    public static byte[] BuildOwnershipTransfer(byte senderSmallId, byte hostSmallId, ushort entityId)
    {
        var payload = new FusionNetWriter(8);

        payload.Write(hostSmallId);
        payload.WriteUInt16(entityId);

        var message = new FusionNetWriter(32);

        message.Write(TagEntityOwnershipRequest);
        message.Write(RelayTypeToServer);
        message.Write(ChannelReliable);
        message.WriteNullable(senderSmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// Reads an ownership response, telling us who now owns an entity.
    /// </summary>
    public static (byte PlayerId, ushort EntityId)? TryReadOwnershipResponse(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (reader.ReadByte() != TagEntityOwnershipResponse)
            {
                return null;
            }

            byte relayType = reader.ReadByte();
            reader.ReadByte();

            if (relayType != RelayTypeNone)
            {
                reader.ReadNullableByte();
            }

            reader.ReadInt32();

            return (reader.ReadByte(), reader.ReadUInt16());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Drives a world entity we own. Velocity matters for throwing, the receiving
    /// side extrapolates from it, so a release with velocity reads as a throw.
    /// </summary>
    public static byte[] BuildEntityPoseUpdate(byte senderSmallId, ushort entityId, Vec3 position,
        Quat rotation, Vec3 velocity, Vec3 angularVelocity)
    {
        var payload = new FusionNetWriter(64);

        payload.WriteUInt16(entityId);
        payload.Write((byte)1); // one body

        FusionRigPose.WriteBodyPose(payload, position, rotation, velocity, angularVelocity);

        var message = new FusionNetWriter(96);

        message.Write(TagEntityPoseUpdate);
        message.Write(RelayTypeToOtherClients);
        message.Write(ChannelUnreliable);
        message.WriteNullable(senderSmallId);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }

    /// <summary>
    /// Reads an entity's networked pose: a ushort ID, a body count, then one
    /// BodyPose per body. Validated against a 31 byte capture (2 + 1 + 28).
    /// </summary>
    public static (ushort EntityId, Vec3 Position, Vec3 Velocity)? TryReadEntityPose(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (reader.ReadByte() != TagEntityPoseUpdate)
            {
                return null;
            }

            ReadRouteAndSender(ref reader);

            reader.ReadInt32(); // payload length

            ushort entityId = reader.ReadUInt16();

            byte bodyCount = reader.ReadByte();

            if (bodyCount == 0)
            {
                return null;
            }

            // Only the first body is needed to locate the object.
            var body = FusionRigPose.ReadBodyPose(ref reader);

            return (entityId, body.Position, body.Velocity);
        }
        catch
        {
            return null;
        }
    }

    public sealed record ConnectionResponseInfo(
        ulong PlatformID,
        byte SmallID,
        string? AvatarBarcode,
        byte[] AvatarStats,
        bool IsInitialJoin);

    /// <summary>
    /// Parses a ConnectionResponse. The host sends one of these for the joining client
    /// and one for every player already present, so this yields both our assigned
    /// SmallID and real avatar proportions taken straight off the wire.
    /// </summary>
    public static ConnectionResponseInfo? TryReadConnectionResponse(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new FusionNetReader(message);

            if (reader.ReadByte() != TagConnectionResponse)
            {
                return null;
            }

            ReadRouteAndSender(ref reader);

            reader.ReadInt32(); // payload length

            // PlayerID
            ulong platformId = reader.ReadUInt64();
            byte smallId = reader.ReadByte();

            int metadataCount = reader.ReadInt32();

            for (var i = 0; i < metadataCount; i++)
            {
                reader.ReadString();
                reader.ReadString();
            }

            int equippedCount = reader.ReadInt32();

            for (var i = 0; i < equippedCount; i++)
            {
                reader.ReadString();
            }

            string? barcode = reader.ReadString();

            var stats = reader.ReadRaw(AvatarStatsSize).ToArray();

            bool isInitialJoin = reader.ReadBool();

            return new ConnectionResponseInfo(platformId, smallId, barcode, stats, isInitialJoin);
        }
        catch
        {
            return null;
        }
    }
}
