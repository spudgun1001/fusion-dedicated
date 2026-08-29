using System.Globalization;

namespace FusionDedicated.Server;

/// <summary>
/// Reads the container's own limits. Inside a container /proc describes the whole
/// node, so the panel would otherwise graph the host's memory rather than the
/// server's allowance.
/// </summary>
public static class CgroupReader
{
    public static long? ParseMemoryLimit(string? contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return null;
        }

        string trimmed = contents.Trim();

        if (trimmed == "max")
        {
            return null;
        }

        if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes))
        {
            return null;
        }

        return bytes > 0 ? bytes : null;
    }

    /// <summary>Parses cgroup v2 "cpu.max", which holds a quota and a period.</summary>
    public static int? ParseCpuQuota(string? cpuMax)
    {
        if (string.IsNullOrWhiteSpace(cpuMax))
        {
            return null;
        }

        var parts = cpuMax.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length != 2 ? null : ParseV1CpuQuota(parts[0], parts[1]);
    }

    public static int? ParseV1CpuQuota(string? quota, string? period)
    {
        if (string.IsNullOrWhiteSpace(quota) || string.IsNullOrWhiteSpace(period))
        {
            return null;
        }

        if (!long.TryParse(quota.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long q)
            || !long.TryParse(period.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long p))
        {
            return null;
        }

        if (q <= 0 || p <= 0)
        {
            return null;
        }

        return Math.Max(1, (int)Math.Ceiling((double)q / p));
    }
}
