using FusionDedicated.Web;

namespace FusionDedicated.Tests.Web;

public class DashboardAuthTests
{
    [Theory]
    [InlineData("hunter2", "hunter2", true)]
    [InlineData("hunter2", "hunter3", false)]
    [InlineData("hunter2", "hunter22", false)]
    [InlineData("", "", true)]
    [InlineData(null, "hunter2", false)]
    [InlineData("hunter2", null, false)]
    [InlineData(null, null, false)]
    public void ConstantTimeEquals_compares_correctly(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, DashboardAuth.ConstantTimeEquals(a, b));
    }
}
