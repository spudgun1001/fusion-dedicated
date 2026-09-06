namespace FusionDedicated;

/// <summary>
/// Mirrors LabFusion's PermissionLevel. Values must match, clients compare against
/// the numbers we publish in the lobby info.
/// </summary>
public enum PermissionLevel : sbyte
{
    Guest = -1,
    Default = 0,
    Operator = 1,
    Owner = 2,
}

public static class PermissionLevels
{
    /// <summary>The wire spelling Fusion expects in player metadata.</summary>
    public static string ToFusionString(this PermissionLevel level) => level switch
    {
        PermissionLevel.Guest => "GUEST",
        PermissionLevel.Operator => "OPERATOR",
        PermissionLevel.Owner => "OWNER",
        _ => "DEFAULT",
    };

    public static bool IsAtLeast(this PermissionLevel level, PermissionLevel required)
        => level >= required;

    public static PermissionLevel Clamp(int raw)
        => (PermissionLevel)Math.Clamp(raw, -1, 2);
}
