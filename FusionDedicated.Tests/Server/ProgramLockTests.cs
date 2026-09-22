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
        string startup = ProgramSource.Between("await pumpTask;", "// ---- main loop ----");

        Assert.Equal(1, source.Split("new Dashboard(").Length - 1);
        Assert.Equal(1, source.Split("dashboard.Start();").Length - 1);
        Assert.Contains("new Dashboard(", startup);
        Assert.Contains("dashboard.Start();", startup);
    }

    [Fact]
    public void Plugins_load_under_the_world_lock_before_the_console_starts()
    {
        string startup = ProgramSource.Between("// ---- main loop ----", "rcon.Start();");

        int load = startup.IndexOf("server.Exclusive(() => plugins.LoadAll())", StringComparison.Ordinal);
        int console = startup.IndexOf("StdinCommands.Start(", StringComparison.Ordinal);

        Assert.True(load >= 0, "plugins do not load under the world lock");
        Assert.True(console > load, "the console starts before the plugins have loaded");
    }

    [Fact]
    public void Plugin_unseat_stays_direct_and_seats_are_looked_up()
    {
        string actions = ProgramSource.Between("new ServerPluginActions(", "var pluginPanel");
        string world = ProgramSource.Between("var pluginWorld = new PluginWorld", "var plugins = new PluginHost(");

        Assert.Contains("platformId => server.UnseatForPlugin(platformId)", actions);
        Assert.Contains("SeatOfLookup = server.SeatOfPlayer,", world);
    }

    [Fact]
    public void Plugins_can_ask_which_entity_a_level_object_became()
    {
        string world = ProgramSource.Between("var pluginWorld = new PluginWorld", "var plugins = new PluginHost(");

        Assert.Contains("SceneEntityLookup = server.SceneEntityOf,", world);
    }
}
