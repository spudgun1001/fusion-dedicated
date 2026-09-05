namespace FusionDedicated.Server;

/// <summary>
/// Wording for combat lines. The server never sees a death, because health lives
/// on the client being hit, so the honest report is who hit whom for how much.
/// </summary>
public static class CombatLog
{
    public const string Level = "DAMAGE";

    public static bool IsWorthLogging(float damage) => damage > 0f;

    public static string Describe(string attacker, string? target, float damage)
        => $"{attacker} hit {(string.IsNullOrWhiteSpace(target) ? "someone" : target)} " +
           $"for {damage:0}";
}
