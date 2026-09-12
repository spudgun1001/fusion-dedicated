using FusionDedicated.Server;
using FusionDedicated.Tests.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Deferred sends wait on a clock a test can move, so the delays after joining can be stepped through.</summary>
public class DeferredClockTests
{
    [Fact]
    public void Deferred_work_reads_the_servers_clock()
    {
        string defer = FusionServerSource.Method("private void Defer(");
        string pump = FusionServerSource.Method("public void PumpDeferred(");

        Assert.Contains("Clock()", defer);
        Assert.Contains("Clock()", pump);
        Assert.DoesNotContain("DateTime.UtcNow", defer);
        Assert.DoesNotContain("DateTime.UtcNow", pump);
    }

    [Fact]
    public void The_clock_is_the_wall_clock_unless_a_test_sets_it()
    {
        using var server = new FusionServer(new ServerConfig(), new FakeTransport());

        Assert.InRange(server.Clock(), DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }
}
