using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class CgroupSelectionTests
{
    private static Func<string, string?> Files(Dictionary<string, string> map)
        => path => map.TryGetValue(path, out var value) ? value : null;

    [Fact]
    public void Prefers_cgroup_v2_when_present()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory.max"] = "2147483648",
            ["/sys/fs/cgroup/memory.current"] = "536870912",
            ["/sys/fs/cgroup/cpu.max"] = "200000 100000",
        });

        var limits = CgroupReader.TryReadLimits(read);

        Assert.NotNull(limits);
        Assert.Equal(2147483648L, limits!.Value.Total);
        Assert.Equal(2147483648L - 536870912L, limits.Value.Available);
        Assert.Equal(2, limits.Value.Cpus);
        Assert.Equal(CgroupReader.StatsSource.CgroupV2, limits.Value.Source);
    }

    [Fact]
    public void Falls_back_to_cgroup_v1()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory/memory.limit_in_bytes"] = "1073741824",
            ["/sys/fs/cgroup/memory/memory.usage_in_bytes"] = "268435456",
            ["/sys/fs/cgroup/cpu/cpu.cfs_quota_us"] = "400000",
            ["/sys/fs/cgroup/cpu/cpu.cfs_period_us"] = "100000",
        });

        var limits = CgroupReader.TryReadLimits(read);

        Assert.NotNull(limits);
        Assert.Equal(1073741824L, limits!.Value.Total);
        Assert.Equal(4, limits.Value.Cpus);
        Assert.Equal(CgroupReader.StatsSource.CgroupV1, limits.Value.Source);
    }

    [Fact]
    public void Returns_null_when_no_cgroup_files_exist()
    {
        Assert.Null(CgroupReader.TryReadLimits(Files(new())));
    }

    [Fact]
    public void Returns_null_when_memory_is_unlimited()
    {
        var read = Files(new() { ["/sys/fs/cgroup/memory.max"] = "max" });

        Assert.Null(CgroupReader.TryReadLimits(read));
    }

    [Fact]
    public void Missing_cpu_limit_still_reports_memory()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory.max"] = "2147483648",
            ["/sys/fs/cgroup/memory.current"] = "0",
            ["/sys/fs/cgroup/cpu.max"] = "max 100000",
        });

        var limits = CgroupReader.TryReadLimits(read);

        Assert.NotNull(limits);
        Assert.Equal(Environment.ProcessorCount, limits!.Value.Cpus);
    }

    [Fact]
    public void Usage_above_the_limit_never_reports_negative_available()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory.max"] = "1000",
            ["/sys/fs/cgroup/memory.current"] = "2000",
        });

        Assert.Equal(0, CgroupReader.TryReadLimits(read)!.Value.Available);
    }

    [Fact]
    public void A_v1_sentinel_limit_counts_as_unlimited()
    {
        var read = Files(new()
        {
            ["/sys/fs/cgroup/memory/memory.limit_in_bytes"] = "9223372036854771712",
        });

        Assert.Null(CgroupReader.TryReadLimits(read));
    }
}
