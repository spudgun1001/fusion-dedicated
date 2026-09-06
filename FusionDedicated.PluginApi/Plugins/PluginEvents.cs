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
    }
}
