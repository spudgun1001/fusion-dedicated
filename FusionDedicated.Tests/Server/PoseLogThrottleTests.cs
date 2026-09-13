using System.Collections.Concurrent;
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

    [Fact]
    public void Forgetting_an_entity_allows_its_key_to_log_straight_away()
    {
        var throttle = new PoseLogThrottle();

        Assert.True(throttle.AllowIgnored(300, sender: 3, Start));
        Assert.False(throttle.AllowIgnored(300, sender: 3, Start.AddSeconds(1)));
        Assert.True(throttle.AllowKeptMoved(300, Start));
        Assert.False(throttle.AllowKeptMoved(300, Start.AddSeconds(1)));

        throttle.Forget(300);

        Assert.True(throttle.AllowIgnored(300, sender: 3, Start.AddSeconds(1)));
        Assert.True(throttle.AllowKeptMoved(300, Start.AddSeconds(1)));
    }

    [Fact]
    public void Forgetting_while_poses_are_logged_on_other_threads_never_throws()
    {
        // An entity removal raises Forget on whichever thread removed it, while
        // the loop keeps logging poses.
        var throttle = new PoseLogThrottle();
        var errors = new ConcurrentQueue<Exception>();

        Parallel.Invoke(
            () => Run(errors, i => throttle.AllowIgnored((ushort)(300 + i % 50), (byte)(i % 8), Start.AddSeconds(i))),
            () => Run(errors, i => throttle.AllowKeptMoved((ushort)(300 + i % 50), Start.AddSeconds(i))),
            () => Run(errors, i => throttle.Forget((ushort)(300 + i % 50))));

        Assert.Empty(errors);
    }

    private static void Run(ConcurrentQueue<Exception> errors, Action<int> work)
    {
        for (int i = 0; i < 200_000; i++)
        {
            try
            {
                work(i);
            }
            catch (Exception e)
            {
                errors.Enqueue(e);
                return;
            }
        }
    }
}
