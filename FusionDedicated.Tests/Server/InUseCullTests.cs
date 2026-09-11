using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A prop somebody is holding or has holstered stays, whichever clock it is on. A
/// holstered mobile went five minutes after it was put away.
/// </summary>
public class InUseCullTests
{
    private const string Mobile = "spudgun1001.Payphone.Spawnable.Mobile";
    private const string Magazine = "c1534c5a-18ce-44aa-a416-63174d616761";

    private static readonly TimeSpan Orphan = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan Inherited = TimeSpan.FromSeconds(900);
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan Ammo = TimeSpan.FromSeconds(120);

    private static readonly HashSet<ushort> Carried = new() { 300 };

    /// <summary>Prop 300, owned by player 3 and untouched for an hour.</summary>
    private static (EntityRegistry Registry, TrackedEntity Prop) Aged(string barcode = Mobile)
    {
        var registry = new EntityRegistry();
        var prop = registry.Register(300, barcode, owner: 3, 0f, 0f, 0f);
        prop.LastUpdate = DateTime.UtcNow.AddHours(-1);
        prop.Source = FusionProtocol.SourceNone;
        return (registry, prop);
    }

    [Fact]
    public void A_carried_prop_is_not_culled_for_being_idle()
    {
        var (registry, _) = Aged();

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle, Ammo, Carried));
        Assert.NotNull(registry.Get(300));
    }

    [Fact]
    public void A_carried_prop_somebody_inherited_is_not_culled()
    {
        var (registry, prop) = Aged();
        prop.Inherited = true;

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle, Ammo, Carried));
    }

    [Fact]
    public void A_carried_prop_with_no_owner_is_not_culled()
    {
        var (registry, prop) = Aged();
        prop.OwnerSmallId = null;

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle, Ammo, Carried));
    }

    [Fact]
    public void A_carried_magazine_is_not_culled_on_the_ammo_clock()
    {
        var (registry, _) = Aged(Magazine);

        Assert.Empty(registry.CullStale(Orphan, Inherited, Idle, Ammo, Carried));
    }

    [Fact]
    public void A_prop_nobody_is_carrying_is_still_culled()
    {
        var (registry, _) = Aged();

        Assert.Equal(new ushort[] { 300 },
            registry.CullStale(Orphan, Inherited, Idle, Ammo, new HashSet<ushort> { 999 }));
    }

    [Fact]
    public void A_carried_prop_is_not_evicted_at_the_cap()
    {
        var (registry, prop) = Aged();
        prop.Inherited = true;

        Assert.Empty(registry.EvictOldest(10, inUse: Carried));
    }

    [Fact]
    public void A_carried_prop_is_not_evicted_on_the_last_resort_pass()
    {
        var (registry, _) = Aged();

        Assert.Empty(registry.EvictOldest(10, anyOwner: true, idleFor: TimeSpan.FromMinutes(2), inUse: Carried));
    }

    [Fact]
    public void A_prop_nobody_is_carrying_is_still_evicted_on_the_last_resort_pass()
    {
        var (registry, _) = Aged();

        Assert.Equal(new ushort[] { 300 },
            registry.EvictOldest(10, anyOwner: true, idleFor: TimeSpan.FromMinutes(2),
                inUse: new HashSet<ushort> { 999 }));
    }
}
