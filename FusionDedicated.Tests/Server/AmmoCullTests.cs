using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Spent magazines are the bulk of what a busy server accumulates, and until now
/// the only thing that removed one while its owner was still playing was the idle
/// timeout, which is off by default and takes every untouched build with it when
/// it is turned on. So a dropped magazine stayed for the rest of the session.
/// </summary>
public class AmmoCullTests
{
    private const string Magazine = "c1534c5a-18ce-44aa-a416-63174d616761";
    private const string Build = "DayTrip.PortableBodymall.Spawnable.Bodymall";

    private static readonly TimeSpan Never = TimeSpan.Zero;
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FifteenMinutes = TimeSpan.FromMinutes(15);

    private static EntityRegistry World(params (ushort Id, string Barcode, int MinutesOld)[] props)
    {
        var registry = new EntityRegistry();

        foreach (var (id, barcode, age) in props)
        {
            var entity = registry.Register(id, barcode, 1, 0, 0, 0);
            entity.LastUpdate = DateTime.UtcNow.AddMinutes(-age);

            // Spawned rather than taken from the pouch, so these keep testing the
            // ammo clock itself rather than the pouch rule.
            entity.Source = FusionProtocol.SourceNone;
        }

        return registry;
    }

    /// <summary>A magazine taken from player 1's ammo pouch, aged.</summary>
    private static EntityRegistry Pouch(int minutesOld)
    {
        var registry = new EntityRegistry();

        var entity = registry.Register(300, Magazine, 1, 0, 0, 0);
        entity.Source = FusionProtocol.SourcePlayer;
        entity.LastUpdate = DateTime.UtcNow.AddMinutes(-minutesOld);

        return registry;
    }

    [Fact]
    public void A_dropped_magazine_goes_even_though_its_owner_is_still_here()
    {
        var registry = World((300, Magazine, 5));

        var removed = registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes);

        Assert.Equal(new ushort[] { 300 }, removed);
    }

    [Fact]
    public void A_build_beside_it_is_left_alone()
    {
        // The whole reason this is separate from the idle timeout.
        var registry = World((300, Magazine, 5), (301, Build, 600));

        var removed = registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes);

        Assert.DoesNotContain(removed, id => id == 301);
    }

    [Fact]
    public void A_magazine_still_being_used_stays()
    {
        var registry = World((300, Magazine, 1));

        Assert.Empty(registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void Zero_leaves_ammunition_to_the_ordinary_rules()
    {
        var registry = World((300, Magazine, 5));

        Assert.Empty(registry.CullStale(TwoMinutes, FifteenMinutes, Never, Never));
    }

    [Fact]
    public void An_inherited_magazine_goes_on_the_shorter_clock_too()
    {
        // Inherited props wait fifteen minutes, which for a magazine is fourteen
        // minutes of somebody's frame rate.
        var registry = World((300, Magazine, 5));
        registry.Get(300)!.Inherited = true;

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void It_never_keeps_a_magazine_longer_than_the_ordinary_rules_would()
    {
        // A longer ammo clock must not rescue an orphan from the orphan timeout.
        var registry = World((300, Magazine, 5));
        registry.SetOwner(300, null);

        // SetOwner stamps the entity, so age it again after orphaning it.
        registry.Get(300)!.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

        var removed = registry.CullStale(TwoMinutes, FifteenMinutes, Never, TimeSpan.FromHours(1));

        Assert.Equal(new ushort[] { 300 }, removed);
    }

    [Fact]
    public void A_magazine_that_came_with_the_level_is_still_never_taken()
    {
        var registry = World((300, Magazine, 5));
        registry.Get(300)!.Discovered = true;

        Assert.Empty(registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void A_magazine_marked_to_survive_a_restart_is_still_never_taken()
    {
        var registry = World((300, Magazine, 5));
        registry.Get(300)!.Persistent = true;

        Assert.Empty(registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void An_orphan_timeout_of_zero_still_means_at_once()
    {
        // Adding the ammo clock introduced a guard on the shared branch, and this
        // is the behaviour that guard must not change.
        var registry = World((300, Build, 5));
        registry.SetOwner(300, null);

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(Never, FifteenMinutes, Never, Never));
    }

    [Fact]
    public void A_magazine_in_a_gun_is_never_taken()
    {
        // The report that started this: guns coming out of holsters empty. A
        // magazine in a gun is kinematic, so Fusion sleeps it and it stops
        // sending poses, and the clock counts from the last pose.
        var registry = World((300, Magazine, 60));
        registry.SetAttached(300, true);

        Assert.Empty(registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void A_holstered_gun_keeps_what_is_in_it_however_long_it_sits()
    {
        var registry = World((300, Magazine, 6000));
        registry.SetAttached(300, true);

        Assert.Empty(registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void The_same_magazine_goes_once_it_is_ejected()
    {
        var registry = World((300, Magazine, 5));
        registry.SetAttached(300, true);
        registry.SetAttached(300, false);

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void Being_in_a_gun_does_not_rescue_it_from_the_ordinary_rules()
    {
        // Its owner left and nobody took it over, so it is an orphan like any
        // other. Only the shorter ammo clock is held off.
        var registry = World((300, Magazine, 5));
        registry.SetAttached(300, true);
        registry.SetOwner(300, null);
        registry.Get(300)!.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void Attaching_something_that_is_not_here_is_ignored()
    {
        var registry = World((300, Magazine, 5));

        registry.SetAttached(999, true);

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void A_modded_magazine_is_recognised_by_name()
    {
        var registry = World((300, "SomePack.Spawnable.MagAK47", 5));

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void A_pouch_magazine_its_owner_still_has_is_not_on_the_ammo_clock()
    {
        // Holstered, it goes kinematic and sends no poses, so the clock ran out
        // on a magazine sitting on somebody's body.
        var registry = Pouch(5);

        Assert.Empty(registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void A_spawned_magazine_is_still_on_the_ammo_clock()
    {
        var registry = Pouch(5);
        registry.Get(300)!.Source = FusionProtocol.SourceNone;

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void An_orphaned_pouch_magazine_goes_on_the_ammo_clock()
    {
        // The orphan clock is set longer here, so only the ammo clock can take it.
        var registry = Pouch(0);
        registry.SetOwner(300, null);
        registry.Get(300)!.LastUpdate = DateTime.UtcNow.AddMinutes(-5);

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(FifteenMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void A_pouch_magazine_left_behind_for_somebody_else_goes_on_the_ammo_clock()
    {
        var registry = Pouch(5);
        registry.Get(300)!.Inherited = true;

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void A_pouch_magazine_its_owner_stopped_simulating_goes_on_the_ammo_clock()
    {
        var registry = Pouch(5);
        registry.SetCulledForOwner(300, true);

        Assert.Equal(new ushort[] { 300 }, registry.CullStale(TwoMinutes, FifteenMinutes, Never, TwoMinutes));
    }

    [Fact]
    public void The_pouch_rule_does_not_hold_off_the_idle_clock()
    {
        var registry = Pouch(10);

        Assert.Equal(new ushort[] { 300 },
            registry.CullStale(TwoMinutes, FifteenMinutes, TimeSpan.FromMinutes(5), TwoMinutes));
    }
}
