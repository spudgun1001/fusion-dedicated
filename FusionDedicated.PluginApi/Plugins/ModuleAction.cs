namespace FusionDedicated.Plugins;

/// <summary>A module message on its way through the server.</summary>
public readonly record struct ModuleRequest(
    ulong PlatformId, string Name, PermissionLevel Rank, long HandlerTag, byte[] Payload);

public enum ModuleActionKind
{
    /// <summary>Carry on as though no plugin were there.</summary>
    Forward = 0,

    /// <summary>Nothing more happens to this message.</summary>
    Drop = 1,

    /// <summary>Send this payload to the clients instead of the original.</summary>
    Rewrite = 2,

    /// <summary>Send this payload back to whoever sent the message.</summary>
    Reply = 3,
}

/// <summary>
/// What a plugin does with a module message. Forward is how a plugin that only
/// watches hands control back, so the server's own handling still runs.
/// </summary>
public sealed record ModuleAction(ModuleActionKind Kind, byte[] Payload)
{
    public static readonly ModuleAction Forward =
        new(ModuleActionKind.Forward, Array.Empty<byte>());

    public static readonly ModuleAction Drop =
        new(ModuleActionKind.Drop, Array.Empty<byte>());

    /// <summary>
    /// An empty payload becomes a drop, because an empty module message reads to a
    /// client as a malformed one, which is worse than sending none.
    /// </summary>
    public static ModuleAction Rewrite(byte[] payload)
        => payload is { Length: > 0 }
            ? new ModuleAction(ModuleActionKind.Rewrite, payload)
            : Drop;

    public static ModuleAction Reply(byte[] payload)
        => payload is { Length: > 0 }
            ? new ModuleAction(ModuleActionKind.Reply, payload)
            : Drop;
}
