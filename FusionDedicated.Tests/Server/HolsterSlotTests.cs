using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Which weapon is on which hip.
///
/// The report that started this: take somebody's gun off their holster, put it
/// on your own, and they still had it. Taking a gun from another player's slot
/// arrives as an insert naming your slot and says nothing about theirs, so
/// recording both left the same gun on two people for anybody who joined
/// afterwards.
/// </summary>
public class HolsterSlotTests
{
    private const ushort TheirHip = 700;
    private const ushort MyHip = 800;
    private const ushort Gun = 300;

    [Fact]
    public void A_holstered_gun_is_remembered()
    {
        var slots = new HolsterSlots();

        slots.Insert(TheirHip, 0, Gun);

        Assert.Equal((TheirHip, (byte)0), slots.Find(Gun));
    }

    [Fact]
    public void Taking_it_onto_your_own_hip_takes_it_off_theirs()
    {
        var slots = new HolsterSlots();
        slots.Insert(TheirHip, 0, Gun);

        slots.Insert(MyHip, 0, Gun);

        Assert.Equal((MyHip, (byte)0), slots.Find(Gun));
        Assert.Equal(1, slots.Count);
    }

    [Fact]
    public void Their_holster_is_told_to_nobody_once_it_is_empty()
    {
        // The join replay walks this list, which is how a newcomer saw the same
        // gun on two people.
        var slots = new HolsterSlots();
        slots.Insert(TheirHip, 0, Gun);
        slots.Insert(MyHip, 0, Gun);

        Assert.Single(slots.All());
        Assert.Equal(MyHip, slots.All()[0].Slot);
    }

    [Fact]
    public void Two_guns_can_share_one_rig_on_different_slots()
    {
        var slots = new HolsterSlots();

        slots.Insert(MyHip, 0, Gun);
        slots.Insert(MyHip, 1, 301);

        Assert.Equal(2, slots.Count);
        Assert.Equal((MyHip, (byte)0), slots.Find(Gun));
        Assert.Equal((MyHip, (byte)1), slots.Find(301));
    }

    [Fact]
    public void Holstering_the_same_gun_in_the_same_slot_twice_changes_nothing()
    {
        var slots = new HolsterSlots();

        slots.Insert(MyHip, 0, Gun);
        slots.Insert(MyHip, 0, Gun);

        Assert.Equal(1, slots.Count);
    }

    [Fact]
    public void A_slot_holds_the_last_gun_put_in_it()
    {
        var slots = new HolsterSlots();

        slots.Insert(MyHip, 0, Gun);
        slots.Insert(MyHip, 0, 301);

        Assert.Equal(301, slots.All()[0].Weapon);
        Assert.Null(slots.Find(Gun));
    }

    [Fact]
    public void Drawing_it_says_what_left()
    {
        var slots = new HolsterSlots();
        slots.Insert(MyHip, 0, Gun);

        Assert.Equal(Gun, slots.Drop(MyHip, 0));
        Assert.Equal(0, slots.Count);
    }

    [Fact]
    public void Drawing_from_an_empty_slot_says_nothing_left()
    {
        Assert.Null(new HolsterSlots().Drop(MyHip, 0));
    }

    [Fact]
    public void A_gun_that_is_not_holstered_is_nowhere()
    {
        Assert.Null(new HolsterSlots().Find(Gun));
    }

    [Fact]
    public void Slots_on_a_rig_that_has_gone_are_forgotten()
    {
        var slots = new HolsterSlots();
        slots.Insert(TheirHip, 0, Gun);
        slots.Insert(MyHip, 0, 301);

        int gone = slots.ForgetSlots(slot => slot == TheirHip);

        Assert.Equal(1, gone);
        Assert.Equal(1, slots.Count);
        Assert.Equal(MyHip, slots.All()[0].Slot);
    }

    [Fact]
    public void One_slot_can_be_forgotten_for_a_weapon_that_has_gone()
    {
        var slots = new HolsterSlots();
        slots.Insert(MyHip, 0, Gun);

        Assert.True(slots.Forget(MyHip, 0));
        Assert.False(slots.Forget(MyHip, 0));
    }

    [Fact]
    public void It_stops_taking_more_than_it_will_hold()
    {
        var slots = new HolsterSlots();

        for (int i = 0; i < HolsterSlots.Capacity; i++)
        {
            slots.Insert((ushort)(1000 + i), 0, (ushort)(2000 + i));
        }

        Assert.False(slots.Insert(9999, 0, 9998));
        Assert.Equal(HolsterSlots.Capacity, slots.Count);
    }

    [Fact]
    public void A_slot_already_held_can_still_be_written_when_full()
    {
        // Replacing what is in a slot does not need room for another.
        var slots = new HolsterSlots();

        for (int i = 0; i < HolsterSlots.Capacity; i++)
        {
            slots.Insert((ushort)(1000 + i), 0, (ushort)(2000 + i));
        }

        Assert.True(slots.Insert(1000, 0, 5555));
        Assert.Equal(5555, slots.All().First(s => s.Slot == 1000).Weapon);
    }
}
