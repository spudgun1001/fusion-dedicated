namespace FusionDedicated.Tests.Web;

/// <summary>
/// The panel handles requests on its own thread, so every read or write of the world
/// takes the world lock, while plugin code and HTTP writes stay outside it.
/// </summary>
public class DashboardLockTests
{
    [Theory]
    [InlineData("private void HandleKick(")]
    [InlineData("private void HandleBanNote(")]
    [InlineData("private void HandleBan(")]
    [InlineData("private void HandleMute(")]
    [InlineData("private void HandleUnban(")]
    [InlineData("private void HandlePermission(")]
    [InlineData("private void HandleLevel(")]
    [InlineData("private void HandleLevels(")]
    [InlineData("private void HandleGather(")]
    [InlineData("private void HandleDespawn(")]
    [InlineData("private void HandlePersist(")]
    [InlineData("private void HandlePurge(")]
    [InlineData("private void HandleRestart(")]
    [InlineData("private void HandleSettings(")]
    public void Each_world_handler_takes_the_world_lock(string signature)
        => Assert.Contains("_server.Exclusive", DashboardSource.Method(signature));

    /// <summary>One endpoint's case in Handle, up to the case that follows it.</summary>
    private static string Case(string route, string next)
    {
        string handle = DashboardSource.Method("private void Handle(HttpListenerContext context)");
        int start = handle.IndexOf($"case \"{route}\":", StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{route}' is not handled");

        int end = handle.IndexOf($"case \"{next}\":", start, StringComparison.Ordinal);

        Assert.True(end > start, $"'{next}' does not follow '{route}'");

        return handle[start..end];
    }

    [Theory]
    [InlineData("/api/state", "/api/kick")]
    [InlineData("/api/clear", "/api/gather")]
    [InlineData("/api/modules", "/api/plugins")]
    public void The_state_clear_and_modules_endpoints_take_the_world_lock(string route, string next)
        => Assert.Contains("_server.Exclusive(", Case(route, next));

    [Fact]
    public void The_audit_endpoint_reads_outside_the_world_lock()
        => Assert.DoesNotContain("_server.Exclusive(", Case("/api/audit", "/api/purge"));

    [Fact]
    public void Plugin_code_never_runs_under_the_world_lock()
    {
        string source = DashboardSource.Text();

        Assert.DoesNotContain("Exclusive(() => VisiblePage", source);
        Assert.DoesNotContain("Exclusive(() => VisiblePlugins", source);
        Assert.DoesNotContain("Exclusive(() => MayInvoke", source);
        Assert.DoesNotContain("Exclusive(() => PluginPanel", source);
        Assert.DoesNotContain("Exclusive(() => PluginHost", source);
    }

    [Fact]
    public void A_request_that_throws_is_still_answered()
    {
        string loop = DashboardSource.Method("private async Task LoopAsync(");

        Assert.Contains("context.Response.StatusCode = 500;", loop);
        Assert.Contains("context.Response.Close();", loop);
    }
}
