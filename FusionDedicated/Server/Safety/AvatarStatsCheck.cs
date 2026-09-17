using System.Buffers.Binary;
using System.Globalization;
using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Server.Safety;

/// <param name="Problem">The first bad field and its value, or null when the block is fine.</param>
/// <param name="Impossible">No real avatar has a value like this, so only a modded client sends it.</param>
public readonly record struct AvatarStatsVerdict(string? Problem, bool Impossible);

/// <summary>
/// Checks the proportions block an avatar swap carries. A modded client sent masses of
/// 100,000,000, and the physics flung the player it was sent to.
/// </summary>
public static class AvatarStatsCheck
{
    /// <summary>The 105 floats in the order SerializedAvatarStats.Serialize writes them.</summary>
    public static readonly IReadOnlyList<string> FieldNames = BuildNames();

    /// <summary>Fusion scales the Calibration avatar by height over this, and ignores localScale for it.</summary>
    public const float CalibrationHeight = 1.76f;

    private const float ImpossibleSize = 100000f;
    private const float Backstop = 1000f;
    private const float MassFloor = 1f;

    private static readonly int PartsStart = FieldNames.Count - 6;
    private static readonly int Total = FieldNames.Count - 1;
    private static readonly int Height = FieldNames.ToList().IndexOf("height");
    private static readonly bool[] NotNegative = FieldNames.Select(IsRadiusOrLength).ToArray();

    public static string? Problem(ReadOnlySpan<byte> stats, ServerConfig limits) => Check(stats, limits).Problem;

    public static AvatarStatsVerdict Check(ReadOnlySpan<byte> stats, ServerConfig limits)
    {
        if (stats.Length != FusionProtocol.AvatarStatsSize)
        {
            return new AvatarStatsVerdict($"a stats block of {stats.Length} bytes, not {FusionProtocol.AvatarStatsSize}", true);
        }

        for (var i = 0; i < FieldNames.Count; i++)
        {
            if (Impossible(i, Read(stats, i)) is { } why)
            {
                return new AvatarStatsVerdict($"{FieldNames[i]} {Show(Read(stats, i))}, {why}", true);
            }
        }

        for (var i = 0; i < FieldNames.Count; i++)
        {
            float value = Read(stats, i);
            var (min, max) = Bounds(i, limits);

            if (value < min || value > max)
            {
                return new AvatarStatsVerdict($"{FieldNames[i]} {Show(value)}, {(value < min ? "under " + Show(min) : "over " + Show(max))}", false);
            }
        }

        return new AvatarStatsVerdict(null, false);
    }

    /// <summary>A copy with every value pulled inside the limits. Only for a block with nothing impossible in it.</summary>
    public static byte[] Clamp(ReadOnlySpan<byte> stats, ServerConfig limits)
    {
        byte[] clamped = stats.ToArray();

        for (var i = 0; i < FieldNames.Count && clamped.Length == FusionProtocol.AvatarStatsSize; i++)
        {
            var (min, max) = Bounds(i, limits);

            if (min > max)
            {
                continue;
            }

            BinaryPrimitives.WriteSingleBigEndian(clamped.AsSpan(i * 4, 4), Math.Clamp(Read(clamped, i), min, max));
        }

        return clamped;
    }

    /// <summary>A setting that would refuse every avatar, or null.</summary>
    public static string? SettingsProblem(ServerConfig limits)
        => limits.MinAvatarScale > 0f && limits.MaxAvatarScale > 0f && limits.MinAvatarScale > limits.MaxAvatarScale
            ? $"MinAvatarScale {Show(limits.MinAvatarScale)} is over MaxAvatarScale {Show(limits.MaxAvatarScale)}, " +
              "so every avatar swap is dropped"
            : null;

    private static string? Impossible(int i, float value)
    {
        if (!float.IsFinite(value))
        {
            return "not a number";
        }

        if (float.IsSubnormal(value))
        {
            return "subnormal";
        }

        if (Math.Abs(value) > ImpossibleSize)
        {
            return $"over {Show(ImpossibleSize)}";
        }

        if (i >= PartsStart && value < 0f)
        {
            return "a negative mass";
        }

        return i == Total && value <= 0f ? "no mass at all" : null;
    }

    /// <summary>The range one field must sit in. A limit setting of 0 or less leaves that side open.</summary>
    private static (float Min, float Max) Bounds(int i, ServerConfig limits)
    {
        float lowScale = limits.MinAvatarScale > 0f ? limits.MinAvatarScale : float.NegativeInfinity;
        float highScale = limits.MaxAvatarScale > 0f ? limits.MaxAvatarScale : float.PositiveInfinity;

        if (i < 3)
        {
            return (lowScale, highScale);
        }

        if (i == Total)
        {
            return (MassFloor, limits.MaxAvatarMass > 0f ? limits.MaxAvatarMass : float.PositiveInfinity);
        }

        if (i >= PartsStart)
        {
            return (0f, limits.MaxAvatarPartMass > 0f ? limits.MaxAvatarPartMass : float.PositiveInfinity);
        }

        float min = NotNegative[i] ? 0f : -Backstop;
        float max = Backstop;

        if (i == Height)
        {
            min = Math.Max(min, lowScale * CalibrationHeight);
            max = Math.Min(max, highScale * CalibrationHeight);
        }

        return (min, max);
    }

    private static bool IsRadiusOrLength(string name)
        => (name.Contains("Ellipse") && !name.EndsWith("Bias"))
        || (name.EndsWith("Length") && name != "palmOffsetLength")
        || name is "height" or "eyeHeight" or "skullHeight" or "chestHeight" or "pelvisHeight";

    // Fusion writes floats big-endian.
    private static float Read(ReadOnlySpan<byte> stats, int i) => BinaryPrimitives.ReadSingleBigEndian(stats.Slice(i * 4, 4));

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
