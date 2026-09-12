using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>The pure rate-limit decision behind the pose-ownership log lines.</summary>
public class PoseLogThrottleTests
{
    private static readonly DateTime Start = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_first_time_is_allowed()
        => Assert.True(PoseLogThrottle.ShouldLog(last: null, now: Start));

    [Fact]
    public void A_second_time_inside_ten_seconds_is_refused()
        => Assert.False(PoseLogThrottle.ShouldLog(last: Start, now: Start.AddSeconds(5)));

    [Fact]
    public void A_second_time_after_ten_seconds_is_allowed_again()
        => Assert.True(PoseLogThrottle.ShouldLog(last: Start, now: Start.AddSeconds(10)));
}
