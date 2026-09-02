using System.Globalization;

namespace FusionDedicated.Server.Bans;

/// <summary>
/// Parses the durations a moderator types: 30m, 2h, 7d, 1w, a bare number of
/// minutes, or a word meaning permanent.
/// </summary>
public static class BanDuration
{
    private static readonly string[] Permanent = { "permanent", "perm", "forever", "0" };

    /// <summary>How long the ban lasts, or null for permanent.</summary>
    public static TimeSpan? TryParse(string text)
    {
        string trimmed = text.Trim().ToLowerInvariant();

        if (Permanent.Contains(trimmed))
        {
            return null;
        }

        char suffix = trimmed.Length > 0 ? trimmed[^1] : ' ';
        string digits = char.IsLetter(suffix) ? trimmed[..^1] : trimmed;

        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value <= 0)
        {
            return null;
        }

        return char.IsLetter(suffix)
            ? suffix switch
            {
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                'w' => TimeSpan.FromDays(value * 7),
                _ => null,
            }
            : TimeSpan.FromMinutes(value);
    }

    /// <summary>
    /// Whether this is a duration at all. Distinguishes "permanent" from a typo,
    /// which both parse to null but mean very different things.
    /// </summary>
    public static bool IsRecognised(string text)
    {
        string trimmed = text.Trim().ToLowerInvariant();

        return Permanent.Contains(trimmed) || TryParse(trimmed) != null;
    }
}
