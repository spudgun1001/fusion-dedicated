using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>A native receive failure said once, then at most once a minute, so a stuck socket cannot fill the log.</summary>
public class ReceiveFailureLogTests
{
    private static readonly DateTime Start = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_first_failure_is_reported_at_once()
    {
        string? line = new ReceiveFailureLog().Failed(-1, Start);

        Assert.NotNull(line);
        Assert.Contains("-1", line);
    }

    [Fact]
    public void Failures_inside_a_minute_stay_quiet()
    {
        var log = new ReceiveFailureLog();
        log.Failed(-1, Start);

        Assert.Null(log.Failed(-1, Start.AddSeconds(1)));
        Assert.Null(log.Failed(-1, Start.AddSeconds(59)));
    }

    [Fact]
    public void After_a_minute_the_report_counts_every_failure_since_the_last()
    {
        var log = new ReceiveFailureLog();
        log.Failed(-1, Start);
        log.Failed(-1, Start.AddSeconds(10));
        log.Failed(-1, Start.AddSeconds(20));

        string? line = log.Failed(-2, Start.AddMinutes(1));

        Assert.NotNull(line);
        Assert.Contains("3 times", line);
        Assert.Contains("-2", line);
    }

    [Fact]
    public void The_transport_reports_a_negative_receive_count()
    {
        string source = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FusionDedicated", "Server", "SteamSocketTransport.cs"))
            .Replace("\r\n", "\n");

        Assert.Contains("if (count < 0)", source);
        Assert.Contains("_receiveFailures.Failed(count, DateTime.UtcNow)", source);
    }
}
