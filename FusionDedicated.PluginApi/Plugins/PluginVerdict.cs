namespace FusionDedicated.Plugins;

/// <summary>
/// Whether a plugin will let something happen. The same shape as the server's own
/// gates, so a plugin refusal reads the same way in the log as a built-in one.
/// </summary>
public sealed record PluginVerdict(bool Allowed, string Reason)
{
    public static readonly PluginVerdict Allow = new(true, "");

    public static PluginVerdict Refuse(string reason) => new(false, reason);
}
