using System.Globalization;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Server;

/// <summary>
/// Detailed log lines about ammunition, so a magazine that vanished can be traced
/// to the rule that took it. Anything that is not ammunition gets no line.
/// </summary>
public static class AmmoDiagnostics
{
    public static string? DescribeCull(TrackedEntity entity, bool ownerOnline, DateTime now)
    {
        if (!Ammunition.IsAmmo(entity.Barcode))
        {
            return null;
        }

        string owner = entity.OwnerSmallId is { } id
            ? $"owner {id} ({(ownerOnline ? "online" : "offline")})"
            : "no owner";

        int seconds = (int)Math.Max(0, (now - entity.LastUpdate).TotalSeconds);

        string distance = entity.OwnerDistanceAtPose is { } metres
            ? metres.ToString("0.0", CultureInfo.InvariantCulture) + " m"
            : "unknown";

        return $"Ammo culled: {NameOf(entity.Barcode)} (entity {entity.Id}), " +
               $"attached {YesNo(entity.Attached)}, source {entity.Source}, {owner}, " +
               $"inherited {YesNo(entity.Inherited)}, {seconds}s since last pose, " +
               $"owner distance at pose {distance}, culled for owner {YesNo(entity.CulledForOwner)}";
    }

    /// <summary>Base game barcodes are GUIDs, so their own name is used when it is known.</summary>
    private static string NameOf(string barcode)
    {
        string trimmed = barcode.Trim();

        return Ammunition.BaseGame.TryGetValue(trimmed, out string? name)
            ? name
            : trimmed[(trimmed.LastIndexOf('.') + 1)..];
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
