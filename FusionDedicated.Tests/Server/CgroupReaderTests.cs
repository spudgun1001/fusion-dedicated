using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class CgroupReaderTests
{
    [Theory]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("  2147483648\n", 2147483648L)]
    public void ParseMemoryLimit_reads_a_byte_count(string contents, long expected)
    {
        Assert.Equal(expected, CgroupReader.ParseMemoryLimit(contents));
    }

    [Theory]
    [InlineData("max")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a number")]
    [InlineData("-1")]
    [InlineData("0")]
    public void ParseMemoryLimit_returns_null_when_unlimited_or_junk(string? contents)
    {
        Assert.Null(CgroupReader.ParseMemoryLimit(contents));
    }

    [Theory]
    [InlineData("200000 100000", 2)]
    [InlineData("100000 100000", 1)]
    [InlineData("150000 100000", 2)]
    [InlineData("50000 100000", 1)]
    public void ParseCpuQuota_divides_quota_by_period(string cpuMax, int expected)
    {
        Assert.Equal(expected, CgroupReader.ParseCpuQuota(cpuMax));
    }

    [Theory]
    [InlineData("max 100000")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("200000")]
    [InlineData("200000 0")]
    public void ParseCpuQuota_returns_null_when_unlimited_or_junk(string? cpuMax)
    {
        Assert.Null(CgroupReader.ParseCpuQuota(cpuMax));
    }

    [Theory]
    [InlineData("200000", "100000", 2)]
    [InlineData("150000", "100000", 2)]
    public void ParseV1CpuQuota_divides_quota_by_period(string quota, string period, int expected)
    {
        Assert.Equal(expected, CgroupReader.ParseV1CpuQuota(quota, period));
    }

    [Theory]
    [InlineData("-1", "100000")]
    [InlineData("200000", "0")]
    [InlineData(null, "100000")]
    public void ParseV1CpuQuota_returns_null_when_unlimited_or_junk(string? quota, string? period)
    {
        Assert.Null(CgroupReader.ParseV1CpuQuota(quota, period));
    }
}
