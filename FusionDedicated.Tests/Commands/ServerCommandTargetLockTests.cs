using FusionDedicated.Commands;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Commands;

/// <summary>Console and RCON run on their own threads, so their commands wait for the loop's pass.</summary>
public class ServerCommandTargetLockTests
{
    // Waits without blocking the test thread, which xUnit1031 warns about.
    [Fact]
    public async Task A_command_waits_while_the_world_lock_is_held()
    {
        using var world = new World();
        var target = new ServerCommandTarget(world.Server);
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();

        var holder = Task.Run(() => world.Server.Exclusive(() =>
        {
            inside.SetResult();
            release.Wait();
        }));

        try
        {
            await inside.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var purge = Task.Run(() => target.Purge(5));

            Assert.NotSame(purge, await Task.WhenAny(purge, Task.Delay(200)));

            release.Set();

            await purge.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
        }

        await holder.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Reloading_plugins_does_not_wait_for_the_world_lock()
    {
        using var world = new World();
        var target = new ServerCommandTarget(world.Server);
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();

        var holder = Task.Run(() => world.Server.Exclusive(() =>
        {
            inside.SetResult();
            release.Wait();
        }));

        try
        {
            await inside.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await Task.Run(() => target.ReloadPlugins()).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
        }

        await holder.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Every_world_command_takes_the_world_lock()
    {
        string source = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..",
                "FusionDedicated", "Commands", "ServerCommandTarget.cs"))
            .Replace("\r\n", "\n");

        Assert.Equal(9, source.Split("_server.Exclusive(").Length - 1);
    }
}
