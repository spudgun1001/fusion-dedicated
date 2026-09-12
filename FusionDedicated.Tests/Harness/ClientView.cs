using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

public sealed class ViewEntity
{
    public required ushort Id { get; init; }
    public string Barcode { get; init; } = "";
    public byte? Owner { get; set; }

    /// <summary>Set while a driver sits in an Atv driver seat, which locks the owner to them.</summary>
    public byte? LockedTo { get; set; }

    public bool CulledForOwner { get; set; }
}

public sealed class ViewPlayer
{
    public required byte SmallId { get; init; }
    public ulong PlatformId { get; init; }
    public string AvatarBarcode { get; init; } = "";
    public bool IsInitialJoin { get; init; }
    public Dictionary<string, string> Metadata { get; } = new();
}

/// <summary>What one player's game believes, built by replaying what the server sent it.</summary>
public sealed class ClientView
{
    private readonly List<byte[]> _heldForLoading = new();
    private readonly List<(ushort Entity, byte Owner)> _pendingDataRequests = new();

    public ClientView(byte smallId) => SmallId = smallId;

    public byte SmallId { get; }

    public bool Loaded { get; private set; }

    public Dictionary<ushort, ViewEntity> Entities { get; } = new();

    /// <summary>Everyone this game has been told about, the server's player 0 included.</summary>
    public Dictionary<byte, ViewPlayer> Players { get; } = new();

    public Dictionary<byte, (ushort Entity, byte Index)> Seats { get; } = new();

    public Dictionary<(ushort Slot, byte Index), ushort> Slots { get; } = new();

    /// <summary>Vehicles with an Atv driver seat at index 0. A modded car without one has no lock.</summary>
    public HashSet<ushort> DriverLockedVehicles { get; } = new();

    public int DroppedWhileLoading { get; private set; }

    public int PosesRejected { get; private set; }

    /// <summary>A real client asks a prop's owner for its state as soon as it builds the prop.</summary>
    public IReadOnlyList<(ushort Entity, byte Owner)> TakeDataRequests()
    {
        var taken = _pendingDataRequests.ToList();
        _pendingDataRequests.Clear();
        return taken;
    }

    /// <summary>DelayWhileTargetLoading messages wait here, the rest of skip-while-loading ones are dropped.</summary>
    public void MarkLoaded()
    {
        Loaded = true;

        foreach (byte[] held in _heldForLoading.ToList())
        {
            Receive(held);
        }

        _heldForLoading.Clear();
    }

    public void Receive(byte[] message)
    {
        if (Envelope.Read(message) is not { } envelope)
        {
            return;
        }

        switch (envelope.Tag)
        {
            case FusionProtocol.TagConnectionResponse:
                Join(message);
                return;

            case FusionProtocol.TagPlayerMetadataResponse:
                MetadataChanged(envelope);
                return;

            case FusionProtocol.TagSpawnResponse:
            case ServerProtocol.TagDespawnResponse:
                if (!Loaded)
                {
                    _heldForLoading.Add(message);
                    return;
                }

                if (envelope.Tag == FusionProtocol.TagSpawnResponse)
                {
                    Spawn(message);
                }
                else
                {
                    Despawn(envelope);
                }

                return;

            case FusionProtocol.TagEntityOwnershipResponse:
                Ownership(envelope);
                return;

            case FusionProtocol.TagEntityPoseUpdate:
            case FusionProtocol.TagEntityCullStatus:
            case FusionProtocol.TagPlayerRepSeat:
            case ModuleProtocol.TagModule:
                if (!Loaded)
                {
                    DroppedWhileLoading++;
                    return;
                }

                if (envelope.Tag == FusionProtocol.TagEntityPoseUpdate)
                {
                    Pose(message, envelope);
                }
                else if (envelope.Tag == FusionProtocol.TagEntityCullStatus)
                {
                    Cull(message, envelope);
                }
                else if (envelope.Tag == FusionProtocol.TagPlayerRepSeat)
                {
                    Seat(message, envelope);
                }
                else
                {
                    Module(message);
                }

                return;
        }
    }

    private void Spawn(byte[] message)
    {
        if (FusionProtocol.TryReadSpawnResponse(message) is not { } spawn)
        {
            return;
        }

        Entities[spawn.EntityId] = new ViewEntity { Id = spawn.EntityId, Barcode = spawn.Barcode ?? "", Owner = spawn.OwnerId };

        if (spawn.OwnerId != SmallId)
        {
            _pendingDataRequests.Add((spawn.EntityId, spawn.OwnerId));
        }
    }

    private void Join(byte[] message)
    {
        if (FusionProtocol.TryReadConnectionResponse(message) is not { } response)
        {
            return;
        }

        var player = new ViewPlayer
        {
            SmallId = response.SmallID,
            PlatformId = response.PlatformID,
            AvatarBarcode = response.AvatarBarcode ?? "",
            IsInitialJoin = response.IsInitialJoin,
        };

        foreach (var (key, value) in response.Metadata)
        {
            player.Metadata[key] = value;
        }

        Players[response.SmallID] = player;
    }

    private void MetadataChanged(Envelope envelope)
    {
        var reader = new FusionNetReader(envelope.Payload);
        byte smallId = reader.ReadByte();
        string? key = reader.ReadString();
        string? value = reader.ReadString();

        if (key != null && Players.TryGetValue(smallId, out var player))
        {
            player.Metadata[key] = value ?? "";
        }
    }

    private void Despawn(Envelope envelope)
    {
        var reader = new FusionNetReader(envelope.Payload);
        reader.ReadByte(); // despawner
        Entities.Remove(reader.ReadUInt16());
    }

    private void Ownership(Envelope envelope)
    {
        var reader = new FusionNetReader(envelope.Payload);
        byte owner = reader.ReadByte();
        ushort id = reader.ReadUInt16();

        if (Entities.TryGetValue(id, out var entity) && entity.LockedTo == null)
        {
            entity.Owner = owner;
        }
    }

    private void Pose(byte[] message, Envelope envelope)
    {
        if (FusionProtocol.TryReadEntityPose(message) is not { } pose
            || !Entities.TryGetValue(pose.EntityId, out var entity)
            || entity.Owner != envelope.Sender)
        {
            PosesRejected++;
        }
    }

    private void Cull(byte[] message, Envelope envelope)
    {
        if (FusionProtocol.TryReadCullStatus(message) is var (id, culled)
            && Entities.TryGetValue(id, out var entity)
            && entity.Owner == envelope.Sender)
        {
            entity.CulledForOwner = culled;
        }
    }

    private void Seat(byte[] message, Envelope envelope)
    {
        if (FusionProtocol.TryReadSeat(message) is not { } seat || envelope.Sender is not { } rider)
        {
            return;
        }

        if (!seat.Ingress)
        {
            if (Seats.Remove(rider, out var left) && Entities.TryGetValue(left.Entity, out var vacated)
                && vacated.LockedTo == rider)
            {
                vacated.LockedTo = null;
            }

            return;
        }

        Seats[rider] = (seat.SeatId, seat.Index);

        if (seat.Index == 0 && DriverLockedVehicles.Contains(seat.SeatId)
            && Entities.TryGetValue(seat.SeatId, out var vehicle))
        {
            vehicle.Owner = rider;
            vehicle.LockedTo = rider;
        }
    }

    private void Module(byte[] message)
    {
        if (ModuleProtocol.TryReadHandlerTag(message) is not { } handler)
        {
            return;
        }

        var change = ModuleProtocol.ReadAttachment(handler, ModuleProtocol.TryReadHandlerPayload(message));

        if (change.Kind == ModuleProtocol.AttachmentKind.SlotInsert)
        {
            foreach (var key in Slots.Where(s => s.Value == change.Entity).Select(s => s.Key).ToList())
            {
                Slots.Remove(key);
            }

            Slots[(change.Slot, change.SlotIndex)] = change.Entity;
        }
        else if (change.Kind == ModuleProtocol.AttachmentKind.SlotDrop)
        {
            Slots.Remove((change.Slot, change.SlotIndex));
        }
    }
}
