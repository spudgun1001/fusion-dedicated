using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginModulesTests
{
    private readonly List<string> _log = new();
    private readonly PluginHealth _health = new();

    private PluginModules Modules() => new(_health, (l, m) => _log.Add(l + " " + m));

    private static ModuleRequest Request(long tag = 42)
        => new(1, "Joel", PermissionLevel.Default, tag, new byte[] { 1, 2, 3 });

    [Fact]
    public void A_tag_nobody_claimed_is_forwarded()
    {
        Assert.Equal(ModuleActionKind.Forward, Modules().Dispatch(Request()).Kind);
        Assert.False(Modules().Claims(42));
    }

    [Fact]
    public void A_claimed_tag_reaches_its_handler()
    {
        var modules = Modules();
        long seen = 0;

        modules.Handle("labrp", 42, r => { seen = r.HandlerTag; return ModuleAction.Drop; });

        Assert.True(modules.Claims(42));
        Assert.Equal(ModuleActionKind.Drop, modules.Dispatch(Request()).Kind);
        Assert.Equal(42, seen);
    }

    [Fact]
    public void A_handler_only_sees_the_tag_it_claimed()
    {
        var modules = Modules();
        modules.Handle("labrp", 42, _ => ModuleAction.Drop);

        Assert.Equal(ModuleActionKind.Forward, modules.Dispatch(Request(99)).Kind);
    }

    [Fact]
    public void A_rewrite_comes_back_with_its_payload()
    {
        var modules = Modules();
        modules.Handle("labrp", 42, _ => ModuleAction.Rewrite(new byte[] { 9, 9 }));

        var action = modules.Dispatch(Request());

        Assert.Equal(ModuleActionKind.Rewrite, action.Kind);
        Assert.Equal(new byte[] { 9, 9 }, action.Payload);
    }

    [Fact]
    public void A_second_plugin_cannot_take_a_tag_that_is_already_claimed()
    {
        var modules = Modules();
        modules.Handle("first", 42, _ => ModuleAction.Drop);
        modules.Handle("second", 42, _ => ModuleAction.Reply(new byte[] { 1 }));

        // The first one keeps it, and the refusal is said out loud rather than the
        // loser silently never running.
        Assert.Equal(ModuleActionKind.Drop, modules.Dispatch(Request()).Kind);
        Assert.Contains(_log, line => line.Contains("second"));
    }

    [Fact]
    public void A_handler_that_throws_forwards_rather_than_swallowing_the_message()
    {
        var modules = Modules();
        modules.Handle("broken", 42, _ => throw new InvalidOperationException("boom"));

        Assert.Equal(ModuleActionKind.Forward, modules.Dispatch(Request()).Kind);
        Assert.Contains(_log, line => line.Contains("broken"));
    }

    [Fact]
    public void A_plugin_that_keeps_throwing_stops_being_asked()
    {
        var modules = Modules();
        var calls = 0;

        modules.Handle("broken", 42, _ => { calls++; throw new InvalidOperationException("boom"); });

        for (var i = 0; i < 5; i++)
        {
            modules.Dispatch(Request());
        }

        Assert.Equal(PluginHealth.FailuresBeforeDisable, calls);
    }

    [Fact]
    public void A_handler_returning_nothing_is_treated_as_forward()
    {
        var modules = Modules();
        modules.Handle("odd", 42, _ => null!);

        Assert.Equal(ModuleActionKind.Forward, modules.Dispatch(Request()).Kind);
    }

    [Fact]
    public void Removing_a_plugin_frees_the_tags_it_claimed()
    {
        var modules = Modules();
        modules.Handle("labrp", 42, _ => ModuleAction.Drop);

        modules.RemoveAll("labrp");

        Assert.False(modules.Claims(42));
        Assert.Equal(ModuleActionKind.Forward, modules.Dispatch(Request()).Kind);
    }

    [Fact]
    public void Removing_one_plugin_leaves_another_tag_alone()
    {
        var modules = Modules();
        modules.Handle("a", 1, _ => ModuleAction.Drop);
        modules.Handle("b", 2, _ => ModuleAction.Drop);

        modules.RemoveAll("a");

        Assert.False(modules.Claims(1));
        Assert.True(modules.Claims(2));
    }
}
