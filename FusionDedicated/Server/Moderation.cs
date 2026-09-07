namespace FusionDedicated.Server;

/// <summary>Why an in-game moderation command was refused, or that it was not.</summary>
public readonly record struct ModerationVerdict(bool Allowed, string Reason)
{
    public static readonly ModerationVerdict Allow = new(true, "");

    public static ModerationVerdict Refuse(string reason) => new(false, reason);
}

/// <summary>
/// Who may kick, ban or teleport whom from inside the game.
///
/// Two rules, and the second is the one that surprises people. A moderator needs
/// the rank the setting asks for, and on top of that cannot act on somebody
/// ranked at or above themselves. That second rule means one Owner cannot ban
/// another, which reads as banning "not always working" when a server has
/// several Owners: it depends entirely on who is being banned.
///
/// The panel does not use these rules at all. It has its own accounts and its
/// own roles, which is why the same ban goes through there.
/// </summary>
public static class Moderation
{
    public static ModerationVerdict Check(
        string action, PermissionLevel sender, PermissionLevel target, PermissionLevel required)
    {
        if (!sender.IsAtLeast(required))
        {
            return ModerationVerdict.Refuse(
                $"{action} needs {required.ToFusionString()} and they are {sender.ToFusionString()}");
        }

        if (target >= sender)
        {
            return ModerationVerdict.Refuse(
                $"they are {target.ToFusionString()}, which is not below {sender.ToFusionString()}");
        }

        return ModerationVerdict.Allow;
    }
}
