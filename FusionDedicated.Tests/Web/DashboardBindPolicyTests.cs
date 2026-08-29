using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class DashboardBindPolicyTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_hosts_are_recognised(string host)
    {
        Assert.True(DashboardAuth.IsLoopback(host));
    }

    [Theory]
    [InlineData("+")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    public void Public_hosts_are_not_loopback(string host)
    {
        Assert.False(DashboardAuth.IsLoopback(host));
    }

    [Fact]
    public void Public_host_without_a_password_is_refused()
    {
        Assert.NotNull(DashboardAuth.BindRefusalReason("+", ""));
    }

    [Fact]
    public void Public_host_with_a_password_is_allowed()
    {
        Assert.Null(DashboardAuth.BindRefusalReason("+", "hunter2"));
    }

    [Fact]
    public void Loopback_without_a_password_is_allowed()
    {
        Assert.Null(DashboardAuth.BindRefusalReason("localhost", ""));
    }
}
