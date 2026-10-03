namespace FusionDedicated.Plugins;

public readonly record struct SpawnEvent(
    ulong PlatformId, string Name, PermissionLevel Rank, string Barcode, byte Source);

public readonly record struct ToolEvent(
    ulong PlatformId, string Name, PermissionLevel Rank, string Barcode, ushort EntityId);

public readonly record struct AvatarEvent(
    ulong PlatformId, string Name, PermissionLevel Rank, string Barcode);

public readonly record struct JoinEvent(ulong PlatformId, string Name, PermissionLevel Rank);

public readonly record struct LeaveEvent(ulong PlatformId, string Name);

public readonly record struct DamageEvent(
    ulong PlatformId, string Name, ulong TargetPlatformId, float Damage);

public readonly record struct TeleportEvent(ulong PlatformId, string Name, PermissionLevel Rank);

public readonly record struct ConstraintEvent(ulong PlatformId, string Name, PermissionLevel Rank);

public readonly record struct ModerationEvent(
    ulong ActorPlatformId, string Actor, ulong TargetPlatformId, string Action);

public readonly record struct OwnershipEvent(
    ulong PlatformId, byte SmallId, string Name, PermissionLevel Rank,
    ushort EntityId, string Barcode, ulong OwnerPlatformId);

/// <summary>
/// A player sitting in or getting out of a vehicle seat. SeatIndex is the seat's order in the vehicle's entity.
/// An egress is raised only for a seat the server had recorded.
/// </summary>
public readonly record struct SeatEvent(
    ulong PlatformId, byte SmallId, string Name, PermissionLevel Rank,
    ushort EntityId, string Barcode, byte SeatIndex, bool Ingress);

/// <summary>A voice about to reach one listener, from where each pelvis was last seen. Range is how far the server carries this talker, always above zero.</summary>
public readonly record struct VoiceEvent(
    ulong SpeakerPlatformId, ulong ListenerPlatformId,
    float SpeakerX, float SpeakerY, float SpeakerZ,
    float ListenerX, float ListenerY, float ListenerZ,
    float Range);

/// <summary>Why a prop left the world.</summary>
public enum RemovalReason
{
    /// <summary>Staff, a plugin or a player's game removed it.</summary>
    Despawned,

    /// <summary>The server cleared it: the stale timer, the entity cap, or a level change.</summary>
    Cleanup,

    /// <summary>It went with a player who left.</summary>
    Left,
}

/// <summary>A prop that has left the world. Owner is 0 when nobody owned it or the owner has gone.</summary>
public readonly record struct RemovedEvent(ushort EntityId, string Barcode, ulong OwnerPlatformId, RemovalReason Reason);

/// <summary>
/// Everything a plugin can watch. Each is a channel of its own, so subscribing to
/// one costs nothing on the others, and every one can refuse except those that
/// report something already done.
/// </summary>
public sealed class PluginEvents
{
    public PluginEvents(PluginHealth health, Action<string, string> log)
    {
        Spawn = new EventChannel<SpawnEvent>(health, log);
        Tool = new EventChannel<ToolEvent>(health, log);
        Avatar = new EventChannel<AvatarEvent>(health, log);
        Joining = new EventChannel<JoinEvent>(health, log);
        Joined = new EventChannel<JoinEvent>(health, log);
        Left = new EventChannel<LeaveEvent>(health, log);
        Damage = new EventChannel<DamageEvent>(health, log);
        Teleport = new EventChannel<TeleportEvent>(health, log);
        Constraint = new EventChannel<ConstraintEvent>(health, log);
        Moderation = new EventChannel<ModerationEvent>(health, log);
        Ownership = new EventChannel<OwnershipEvent>(health, log);
        Seat = new EventChannel<SeatEvent>(health, log);
        Voice = new EventChannel<VoiceEvent>(health, log);
        Removed = new EventChannel<RemovedEvent>(health, log);
    }

    public EventChannel<SpawnEvent> Spawn { get; }
    public EventChannel<ToolEvent> Tool { get; }
    public EventChannel<AvatarEvent> Avatar { get; }

    /// <summary>Before a slot is given, so a refusal stops the join.</summary>
    public EventChannel<JoinEvent> Joining { get; }

    /// <summary>After the join completed. Refusing here does nothing.</summary>
    public EventChannel<JoinEvent> Joined { get; }

    public EventChannel<LeaveEvent> Left { get; }
    public EventChannel<DamageEvent> Damage { get; }
    public EventChannel<TeleportEvent> Teleport { get; }
    public EventChannel<ConstraintEvent> Constraint { get; }
    public EventChannel<ModerationEvent> Moderation { get; }

    /// <summary>Raised after MayHold, so a plugin sees only a request the server itself already allows.</summary>
    public EventChannel<OwnershipEvent> Ownership { get; }

    /// <summary>Raised for live seats only. Refusing an ingress stands the rider up, and refusing an egress does nothing.</summary>
    public EventChannel<SeatEvent> Seat { get; }

    /// <summary>A voice on its way to a listener. Refusing keeps it from them. Radio, phone and megaphone voices never come here.</summary>
    public EventChannel<VoiceEvent> Voice { get; }

    /// <summary>A prop that has already gone, so refusing does nothing. Raised on whichever thread removed it.</summary>
    public EventChannel<RemovedEvent> Removed { get; }

    /// <summary>Detaches a plugin from everything, so unloading leaves nothing behind.</summary>
    public void RemoveAll(string plugin)
    {
        Spawn.RemoveAll(plugin);
        Tool.RemoveAll(plugin);
        Avatar.RemoveAll(plugin);
        Joining.RemoveAll(plugin);
        Joined.RemoveAll(plugin);
        Left.RemoveAll(plugin);
        Damage.RemoveAll(plugin);
        Teleport.RemoveAll(plugin);
        Constraint.RemoveAll(plugin);
        Moderation.RemoveAll(plugin);
        Ownership.RemoveAll(plugin);
        Seat.RemoveAll(plugin);
        Voice.RemoveAll(plugin);
        Removed.RemoveAll(plugin);
    }
}
