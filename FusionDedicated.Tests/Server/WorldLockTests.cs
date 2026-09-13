using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Server;

/// <summary>The one lock the loop, the panel, the console and RCON share, and work handed to the loop.</summary>
public class WorldLockTests
{
    [Fact]
    public void OnLoop_runs_at_once_on_a_thread_holding_the_lock()
    {
        using var world = new World();
        bool ran = false;

        world.Server.Exclusive(() => world.Server.OnLoop(() => ran = true));

        Assert.True(ran);
    }

    [Fact]
    public void OnLoop_elsewhere_waits_for_the_next_pump()
    {
        using var world = new World();
        bool ran = false;

        world.Server.OnLoop(() => ran = true);

        Assert.False(ran);

        world.Server.PumpDeferred();

        Assert.True(ran);
    }

    [Fact]
    public void Exclusive_keeps_another_thread_out_until_it_is_released()
    {
        using var world = new World();
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var holder = Task.Run(() => world.Server.Exclusive(() =>
        {
            inside.Set();
            release.Wait();
        }));

        inside.Wait();

        var other = Task.Run(() => world.Server.Exclusive(() => 42));

        Assert.False(other.Wait(TimeSpan.FromMilliseconds(200)));

        release.Set();

        Assert.True(other.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(42, other.Result);
        holder.Wait();
    }

    [Fact]
    public void Exclusive_can_be_entered_again_on_the_same_thread()
    {
        using var world = new World();

        Assert.Equal(7, world.Server.Exclusive(() => world.Server.Exclusive(() => 7)));
    }

    [Fact]
    public void A_throw_inside_Exclusive_releases_the_lock()
    {
        using var world = new World();

        Assert.Throws<InvalidOperationException>(
            () => world.Server.Exclusive(() => throw new InvalidOperationException("boom")));

        var other = Task.Run(() => world.Server.Exclusive(() => 1));

        Assert.True(other.Wait(TimeSpan.FromSeconds(5)));
    }
}
