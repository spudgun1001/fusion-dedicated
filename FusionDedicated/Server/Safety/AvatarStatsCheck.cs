using System.Buffers.Binary;
using System.Globalization;
using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Server.Safety;

/// <summary>
/// Checks the proportions block an avatar swap carries. A modded client sent masses of
/// 100,000,000, and the physics flung the player it was sent to.
/// </summary>
public static class AvatarStatsCheck
{
    /// <summary>The 105 floats in the order SerializedAvatarStats.Serialize writes them.</summary>
    public static readonly IReadOnlyList<string> FieldNames = BuildNames();

    /// <summary>What is wrong with the block, naming the first bad field, or null when it is fine.</summary>
    public static string? Problem(ReadOnlySpan<byte> stats, ServerConfig limits)
    {
        if (stats.Length != FusionProtocol.AvatarStatsSize)
        {
            return $"a stats block of {stats.Length} bytes, not {FusionProtocol.AvatarStatsSize}";
        }

        int partsStart = FieldNames.Count - 6;
        int total = FieldNames.Count - 1;

        for (var i = 0; i < FieldNames.Count; i++)
        {
            // Fusion writes floats big-endian.
            float value = BinaryPrimitives.ReadSingleBigEndian(stats.Slice(i * 4, 4));
            string name = FieldNames[i];

            if (!float.IsFinite(value))
            {
                return $"{name} {Show(value)}";
            }

            string? outside = i switch
            {
                < 3 => Outside(value, limits.MinAvatarScale, limits.MaxAvatarScale, minIncluded: true),
                _ when i == total => Outside(value, 0f, limits.MaxAvatarMass, minIncluded: false),
                _ when i >= partsStart => Outside(value, 0f, limits.MaxAvatarPartMass, minIncluded: true),
                _ => null,
            };

            if (outside != null)
            {
                return $"{name} {Show(value)}, {outside}";
            }
        }

        return null;
    }

    private static string? Outside(float value, float min, float max, bool minIncluded)
    {
        if (value < min || (!minIncluded && value <= min))
        {
            return minIncluded ? $"under {Show(min)}" : $"not over {Show(min)}";
        }

        return value > max ? $"over {Show(max)}" : null;
    }

    private static string Show(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string[] BuildNames()
    {
        var names = new List<string> { "localScale.x", "localScale.y", "localScale.z" };

        names.AddRange(new[]
        {
            "headTop", "chinY", "underbustY", "waistY", "highHipY", "crotchBottom",
            "headEllipseX", "jawEllipseX", "neckEllipseX", "chestEllipseX", "waistEllipseX",
            "highHipsEllipseX", "hipsEllipseX",
            "headEllipseZ", "jawEllipseZ", "neckEllipseZ", "sternumEllipseZ", "chestEllipseZ",
            "waistEllipseZ", "highHipsEllipseZ", "hipsEllipseZ",
            "headEllipseNegZ", "jawEllipseNegZ", "neckEllipseNegZ", "sternumEllipseNegZ",
            "chestEllipseNegZ", "waistEllipseNegZ", "highHipsEllipseNegZ", "hipsEllipseNegZ",
        });

        foreach (string ellipse in new[] { "thighUpper", "knee", "calf", "ankle", "upperarm", "elbow", "forearm", "wrist" })
        {
            names.AddRange(new[] { $"{ellipse}Ellipse.XRadius", $"{ellipse}Ellipse.XBias", $"{ellipse}Ellipse.ZRadius", $"{ellipse}Ellipse.ZBias" });
        }

        names.AddRange(new[]
        {
            "eyeHeight", "heightPercent", "c1HeightPercent", "height", "t1HeightPercent", "skullHeight",
            "sacrumHeightPercent", "chestHeight", "chestToShoulderPerc", "pelvisHeight", "legUpperPercent",
            "clavicleLength", "armUpperPercent", "legUpperLength", "armUpperLength", "armLowerPercent",
            "legLowerPercent", "armLowerLength", "legLowerLength", "carpalPercent", "palmOffsetLength",
            "sternumOffsetPercent.x", "sternumOffsetPercent.y", "sternumOffsetPercent.z",
            "footLength", "carpalLength", "armPercent", "shoulderToPalmPercent", "armLength",
            "agility", "speed", "strengthUpper", "strengthLower", "vitality", "intelligence",
            "massArm", "massChest", "massHead", "massLeg", "massPelvis", "massTotal",
        });

        return names.ToArray();
    }
}
