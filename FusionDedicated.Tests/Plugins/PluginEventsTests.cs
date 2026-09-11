using FusionDedicated;
using FusionDedicated.Plugins;

namespace FusionDedicated.Tests.Plugins;

public class PluginEventsTests
{
    private static PluginEvents Events()
        => new(new PluginHealth(), (_, _) => { });

    [Fact]
    public void A_spawn_can_be_refused_by_barcode()
    {
        var events = Events();

        events.Spawn.Subscribe("police", e => e.Barcode.Contains("Nimbus")
            ? PluginVerdict.Refuse("police only")
            : PluginVerdict.Allow);

        Assert.False(events.Spawn.Raise(
            new SpawnEvent(1, "Joel", PermissionLevel.Default, "Mod.Spawnable.NimbusGun", 2)).Allowed);

        Assert.True(events.Spawn.Raise(
            new SpawnEvent(1, "Joel", PermissionLevel.Default, "Mod.Spawnable.Crate", 2)).Allowed);
    }

    [Fact]
    public void An_avatar_can_be_refused_for_somebody_off_the_roster()
    {
        var events = Events();

        events.Avatar.Subscribe("police", e => e.PlatformId == 1
            ? PluginVerdict.Allow
            : PluginVerdict.Refuse("not on the police roster"));

        Assert.True(events.Avatar.Raise(new AvatarEvent(1, "Joel", PermissionLevel.Default, "a")).Allowed);
        Assert.False(events.Avatar.Raise(new AvatarEvent(2, "Kanza", PermissionLevel.Default, "a")).Allowed);
    }

    [Fact]
    public void A_join_can_be_refused()
    {
        var events = Events();
        events.Joining.Subscribe("whitelist", _ => PluginVerdict.Refuse("not invited"));

        Assert.False(events.Joining.Raise(new JoinEvent(1, "Joel", PermissionLevel.Default)).Allowed);
    }

    [Fact]
    public void Leaving_is_only_ever_observed()
    {
        var events = Events();
        var seen = "";

        events.Left.Subscribe("economy", e => { seen = e.Name; return PluginVerdict.Allow; });
        events.Left.Raise(new LeaveEvent(1, "Joel"));

        Assert.Equal("Joel", seen);
    }

    [Fact]
    public void Removing_a_plugin_detaches_it_from_every_event()
    {
        var events = Events();

        events.Spawn.Subscribe("police", _ => PluginVerdict.Refuse("no"));
        events.Avatar.Subscribe("police", _ => PluginVerdict.Refuse("no"));
        events.Damage.Subscribe("police", _ => PluginVerdict.Refuse("no"));

        events.RemoveAll("police");

        Assert.True(events.Spawn.Raise(new SpawnEvent(1, "a", PermissionLevel.Default, "b", 1)).Allowed);
        Assert.True(events.Avatar.Raise(new AvatarEvent(1, "a", PermissionLevel.Default, "b")).Allowed);
        Assert.True(events.Damage.Raise(new DamageEvent(1, "a", 2, 10f)).Allowed);
    }

    [Fact]
    public void An_ownership_request_can_be_refused_by_a_plugin()
    {
        var events = Events();

        events.Ownership.Subscribe("police", e => e.OwnerPlatformId != 0 && e.PlatformId != e.OwnerPlatformId
            ? PluginVerdict.Refuse("held by its owner")
            : PluginVerdict.Allow);

        Assert.False(events.Ownership.Raise(
            new OwnershipEvent(2, 2, "Kanza", PermissionLevel.Default, 400, "a.b.Gun", 1)).Allowed);

        Assert.True(events.Ownership.Raise(
            new OwnershipEvent(1, 1, "Joel", PermissionLevel.Default, 400, "a.b.Gun", 1)).Allowed);
    }

    [Fact]
    public void Removing_a_plugin_detaches_it_from_the_ownership_event()
    {
        var events = Events();

        events.Ownership.Subscribe("police", _ => PluginVerdict.Refuse("no"));
        events.RemoveAll("police");

        Assert.True(events.Ownership.Raise(
            new OwnershipEvent(1, 1, "a", PermissionLevel.Default, 400, "b", 0)).Allowed);
    }
}
