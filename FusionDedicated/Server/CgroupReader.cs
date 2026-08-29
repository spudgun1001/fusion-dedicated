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

    public enum StatsSource
    {
        Host,
        CgroupV1,
        CgroupV2,
    }

    /// <summary>
    /// Memory and CPU as the container sees them, or null when no limit is set. The
    /// file reader is injected so this stays testable off Linux.
    /// </summary>
    public static (long Total, long Available, int Cpus, StatsSource Source)? TryReadLimits(
        Func<string, string?> readFile)
    {
        long? v2 = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory.max"));

        if (v2 is { } totalV2)
        {
            long used = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory.current")) ?? 0;
            int cpus = ParseCpuQuota(readFile("/sys/fs/cgroup/cpu.max")) ?? Environment.ProcessorCount;

            return (totalV2, Math.Max(0, totalV2 - used), cpus, StatsSource.CgroupV2);
        }

        long? v1 = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory/memory.limit_in_bytes"));

        // cgroup v1 signals "no limit" with a sentinel near long.MaxValue rather than
        // the word max, so an implausibly large limit means unlimited.
        if (v1 is { } totalV1 && totalV1 < (1L << 53))
        {
            long used = ParseMemoryLimit(readFile("/sys/fs/cgroup/memory/memory.usage_in_bytes")) ?? 0;
            int cpus = ParseV1CpuQuota(
                readFile("/sys/fs/cgroup/cpu/cpu.cfs_quota_us"),
                readFile("/sys/fs/cgroup/cpu/cpu.cfs_period_us")) ?? Environment.ProcessorCount;

            return (totalV1, Math.Max(0, totalV1 - used), cpus, StatsSource.CgroupV1);
        }

        return null;
    }

    /// <summary>Reads a file, or null if it is missing or unreadable.</summary>
    public static string? ReadFileOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
