namespace FusionDedicated.Tests.Server;

/// <summary>Where the world lock is taken at startup, and which plugin actions are handed to the loop.</summary>
public class ProgramLockTests
{
    [Fact]
    public void Plugin_kick_ban_and_rank_run_on_the_loop()
    {
        string actions = ProgramSource.Between("new ServerPluginActions(", "var pluginPanel");

        Assert.Equal(3, actions.Split("server.OnLoop(").Length - 1);
        Assert.Contains("server.OnLoop(() => server.Ban(id, \"\", reason))", actions);
        Assert.Contains("server.OnLoop(() => server.SetPermission(id, \"\", level))", actions);
        Assert.Contains("server.Kick(target.SmallId, reason)", actions);
    }

    [Fact]
    public void Plugin_despawn_and_spawns_stay_direct()
    {
        string actions = ProgramSource.Between("new ServerPluginActions(", "var pluginPanel");

        Assert.Contains("id => server.RemoveEntity(id)", actions);
        Assert.Contains("server.SpawnForPlugin(barcode, x, y, z, rotation)", actions);
        Assert.Contains("server.SpawnForPlayer(barcode, x, y, z, rotation, platformId)", actions);
        Assert.Contains("server.HolsterForPlugin(id, platformId, index)", actions);
        Assert.DoesNotContain("server.Exclusive(", actions);
    }

    [Fact]
    public void Each_pass_of_the_main_loop_holds_the_world_lock()
    {
        string loop = ProgramSource.Between("while (!quit.IsCancellationRequested)", "await Task.Delay(16);");

        int exclusive = loop.IndexOf("server.Exclusive(() =>", StringComparison.Ordinal);

        Assert.True(exclusive >= 0, "the loop pass does not take the world lock");
        Assert.True(exclusive < loop.IndexOf("SteamAPI.RunCallbacks();", StringComparison.Ordinal));
        Assert.True(exclusive < loop.IndexOf("server.Receive();", StringComparison.Ordinal));
        Assert.True(exclusive < loop.IndexOf("server.PumpDeferred();", StringComparison.Ordinal));
        Assert.True(exclusive < loop.IndexOf("server.Tick();", StringComparison.Ordinal));
    }

    [Fact]
    public void The_shutdown_kicks_hold_the_world_lock()
    {
        string shutdown = ProgramSource.Between("// ---- shutdown ----", "await Task.Delay(400);");

        int exclusive = shutdown.IndexOf("server.Exclusive(", StringComparison.Ordinal);

        Assert.True(exclusive >= 0, "the shutdown kicks do not take the world lock");
        Assert.True(exclusive < shutdown.IndexOf("server.Kick(player.SmallId", StringComparison.Ordinal));
    }

    [Fact]
    public void The_shutdown_runs_queued_plugin_actions_before_the_kicks()
    {
        string shutdown = ProgramSource.Between("// ---- shutdown ----", "await Task.Delay(400);");

        int exclusive = shutdown.IndexOf("server.Exclusive(", StringComparison.Ordinal);
        int pump = shutdown.IndexOf("server.PumpDeferred();", StringComparison.Ordinal);

        Assert.True(exclusive >= 0 && pump > exclusive, "the shutdown does not run queued plugin actions inside the world lock");
        Assert.True(pump < shutdown.IndexOf("server.Kick(player.SmallId", StringComparison.Ordinal));
    }

    [Fact]
    public void The_panel_starts_after_the_startup_pump_has_stopped()
    {
        string source = ProgramSource.Text();

        int pumpStopped = source.IndexOf("await pumpTask;", StringComparison.Ordinal);
        int panelStarts = source.IndexOf("dashboard.Start();", StringComparison.Ordinal);

        Assert.True(pumpStopped >= 0, "the startup pump is not awaited");
        Assert.True(panelStarts > pumpStopped, "the panel starts while the startup pump still runs callbacks");
    }

    [Fact]
    public void Plugins_load_under_the_world_lock()
        => Assert.Contains("server.Exclusive(() => plugins.LoadAll())", ProgramSource.Text());
}
