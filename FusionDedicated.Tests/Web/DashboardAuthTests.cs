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

    private static string Basic(string user, string password)
        => "Basic " + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{user}:{password}"));

    [Fact]
    public void TryParseBasic_reads_user_and_password()
    {
        var parsed = DashboardAuth.TryParseBasic(Basic("admin", "hunter2"));

        Assert.NotNull(parsed);
        Assert.Equal("admin", parsed!.Value.User);
        Assert.Equal("hunter2", parsed.Value.Password);
    }

    [Fact]
    public void TryParseBasic_keeps_colons_in_the_password()
    {
        var parsed = DashboardAuth.TryParseBasic(Basic("admin", "a:b:c"));

        Assert.Equal("a:b:c", parsed!.Value.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic")]
    [InlineData("Basic !!!not base64!!!")]
    [InlineData("Basic bm9jb2xvbg==")]
    public void TryParseBasic_returns_null_for_junk(string? header)
    {
        Assert.Null(DashboardAuth.TryParseBasic(header));
    }

    [Fact]
    public void IsAuthorized_accepts_correct_credentials()
    {
        Assert.True(DashboardAuth.IsAuthorized(Basic("admin", "hunter2"), "admin", "hunter2"));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("wrong", "hunter2")]
    public void IsAuthorized_rejects_wrong_credentials(string user, string password)
    {
        Assert.False(DashboardAuth.IsAuthorized(Basic(user, password), "admin", "hunter2"));
    }

    [Fact]
    public void IsAuthorized_allows_everything_when_no_password_is_set()
    {
        Assert.True(DashboardAuth.IsAuthorized(null, "admin", ""));
    }
}
