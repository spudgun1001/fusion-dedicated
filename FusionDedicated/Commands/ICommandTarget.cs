namespace FusionDedicated.Commands;

public sealed record CommandPlayer(
    ulong PlatformId,
    byte SmallId,
    string Name,
    PermissionLevel Rank,
    int EntityCount);

/// <summary>
/// What a command can do to the server. Abstract so the parser is testable without
/// Steam and both transports share one implementation.
/// </summary>
public interface ICommandTarget
{
    IReadOnlyList<CommandPlayer> Players { get; }

    void SetRank(ulong platformId, string name, PermissionLevel level);

    void Kick(byte smallId, string reason);

    void Ban(ulong platformId, string name, string reason);

    bool Unban(ulong platformId);

    int Purge(byte smallId);

    void SetLevel(string barcode, string title);
}
