using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Steam takes minutes to update on a fresh install, and SteamAPI_Init fails until
/// it has finished. Giving up on the first attempt turns a slow start into a crash.
/// </summary>
public class SteamRetryTests
{
    private static SteamInitResult Run(
        Func<bool> init, int attempts = 5, Action<string>? log = null, Action<int>? slept = null)
        => SteamStartup.InitWithRetry(init, attempts, log ?? (_ => { }), _ => slept?.Invoke(0));

    [Fact]
    public void An_immediate_success_does_not_wait()
    {
        var sleeps = 0;

        var result = Run(() => true, slept: _ => sleeps++);

        Assert.Equal(SteamInitResult.Ok, result);
        Assert.Equal(0, sleeps);
    }

    [Fact]
    public void It_keeps_trying_until_Steam_is_ready()
    {
        var calls = 0;

        var result = Run(() => ++calls >= 3);

        Assert.Equal(SteamInitResult.Ok, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void It_gives_up_after_the_attempt_budget()
    {
        var calls = 0;

        var result = Run(() => { calls++; return false; }, attempts: 4);

        Assert.Equal(SteamInitResult.RefusedByClient, result);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void A_missing_native_library_fails_at_once_rather_than_retrying()
    {
        var calls = 0;

        var result = Run(() => { calls++; throw new DllNotFoundException("steam_api64"); });

        Assert.Equal(SteamInitResult.NativeLibraryMissing, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Progress_is_reported_while_waiting()
    {
        var lines = new List<string>();
        var calls = 0;

        Run(() => ++calls >= 3, log: lines.Add);

        Assert.NotEmpty(lines);
        Assert.Contains(lines, l => l.Contains("Steam", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void It_sleeps_between_attempts_but_not_after_the_last()
    {
        var sleeps = 0;

        Run(() => false, attempts: 3, slept: _ => sleeps++);

        Assert.Equal(2, sleeps);
    }

    [Fact]
    public void An_unrelated_exception_still_propagates()
    {
        Assert.Throws<InvalidOperationException>(
            () => Run(() => throw new InvalidOperationException("something else")));
    }
}
