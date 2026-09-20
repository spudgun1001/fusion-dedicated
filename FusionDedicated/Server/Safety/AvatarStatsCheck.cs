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

    /// <summary>Real avatars keep masses and scale well under this. Everything else is only caught as garbage.</summary>
    private const float ImpossibleMass = 100000f;

    private const float ImpossibleSize = 1000000000f;
    private const float MassFloor = 1f;

    /// <summary>An avatar ported from another game is built about this small, so a floor above it enlarges real ones.</summary>
    private const float PortedScale = 0.01f;

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
            float size = Size(i, value);
            var (min, max) = Bounds(i, limits);

            if (size < min || size > max)
            {
                return new AvatarStatsVerdict($"{FieldNames[i]} {Show(value)}, {(size < min ? "under " + Show(min) : "over " + Show(max))}", false);
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
            float value = Read(clamped, i);
            float size = Math.Clamp(Size(i, value), min, max);

            BinaryPrimitives.WriteSingleBigEndian(clamped.AsSpan(i * 4, 4), Mirrored(i, value) ? -size : size);
        }

        return clamped;
    }

    /// <summary>Names a limit <see cref="Bounds"/> ignores, so a setting doing nothing is not silent.</summary>
    public static IEnumerable<string> SettingWarnings(ServerConfig limits)
    {
        foreach (var (name, value) in Settings(limits))
        {
            if (!float.IsFinite(value))
            {
                yield return $"{name} {Show(value)} is not a finite number, so fix it before trusting the avatar limits";
            }
        }

        if (limits.MinAvatarScale > 0f && limits.MaxAvatarScale > 0f && limits.MinAvatarScale > limits.MaxAvatarScale)
        {
            yield return $"MinAvatarScale {Show(limits.MinAvatarScale)} is over " +
                         $"MaxAvatarScale {Show(limits.MaxAvatarScale)}, so both are ignored until they are fixed";
        }
        else if (limits.MinAvatarScale > PortedScale)
        {
            // The floor pulls a smaller avatar up to it, and only the copy everyone else is sent.
            yield return $"MinAvatarScale {Show(limits.MinAvatarScale)} is over the {Show(PortedScale)} a ported " +
                         "avatar is often built at, so everyone else is sent those avatars larger than their wearer sees them";
        }

        if (limits.MaxAvatarMass > 0f && limits.MaxAvatarMass < MassFloor)
        {
            yield return $"MaxAvatarMass {Show(limits.MaxAvatarMass)} is under the {Show(MassFloor)} every avatar " +
                         "weighs, so it is ignored until it is fixed";
        }
    }

    private static IEnumerable<(string Name, float Value)> Settings(ServerConfig limits)
    {
        yield return (nameof(limits.MinAvatarScale), limits.MinAvatarScale);
        yield return (nameof(limits.MaxAvatarScale), limits.MaxAvatarScale);
        yield return (nameof(limits.MaxAvatarMass), limits.MaxAvatarMass);
        yield return (nameof(limits.MaxAvatarPartMass), limits.MaxAvatarPartMass);
    }

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

        float most = i < 3 || i >= PartsStart ? ImpossibleMass : ImpossibleSize;

        if (Math.Abs(value) > most)
        {
            return $"over {Show(most)}";
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
        var (min, max) = Configured(i, limits);

        // A minimum over its maximum is a typo, and obeying it would refuse every avatar.
        return min > max ? (float.NegativeInfinity, float.PositiveInfinity) : (min, max);
    }

    private static (float Min, float Max) Configured(int i, ServerConfig limits)
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

        float min = NotNegative[i] ? 0f : float.NegativeInfinity;
        float max = float.PositiveInfinity;

        if (i == Height)
        {
            min = Math.Max(min, lowScale * CalibrationHeight);
            max = Math.Min(max, highScale * CalibrationHeight);
        }

        return (min, max);
    }

    /// <summary>A negative scale axis mirrors the avatar rather than shrinking it, and only the three scale floats do that.</summary>
    private static bool Mirrored(int i, float value) => i < 3 && value < 0f;

    /// <summary>What the limits judge: a mirrored axis is measured by its size, with the sign put back afterwards.</summary>
    private static float Size(int i, float value) => Mirrored(i, value) ? -value : value;

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
