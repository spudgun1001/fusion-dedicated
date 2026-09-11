using System.Text.RegularExpressions;
using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A new phone turned up in somebody else's holster and nothing said how. These lines
/// go to the detailed log so the next one can be traced.
/// </summary>
public class HolsterLogTests
{
    private static ModuleProtocol.AttachmentChange Insert() => new(ModuleProtocol.AttachmentKind.SlotInsert, 481, 3, 1);

    private static ModuleProtocol.AttachmentChange Drop() => new(ModuleProtocol.AttachmentKind.SlotDrop, 0, 3, 1);

    [Fact]
    public void Putting_something_in_a_holster_names_who_did_it_and_whose_it_is()
        => Assert.Equal("Holster: Joel put Mobile (entity 481) in Kekkis79's slot 1",
            HolsterLog.Change(Insert(), 481, "Mobile", "Kekkis79", "Joel"));

    [Fact]
    public void Taking_something_out_names_what_the_server_had_recorded()
        => Assert.Equal("Holster: Joel took Mobile (entity 481) out of Kekkis79's slot 1",
            HolsterLog.Change(Drop(), 481, "Mobile", "Kekkis79", "Joel"));

    [Fact]
    public void Emptying_a_slot_the_server_had_nothing_in_says_so()
        => Assert.Equal("Holster: Joel emptied Kekkis79's slot 1, which the server had nothing recorded in",
            HolsterLog.Change(Drop(), null, "", "Kekkis79", "Joel"));

    [Fact]
    public void A_magazine_going_into_a_gun_is_not_a_holster_change()
        => Assert.Null(HolsterLog.Change(
            new ModuleProtocol.AttachmentChange(ModuleProtocol.AttachmentKind.Attach, 300, 301, 0),
            300, "Mag", "Joel", "Joel"));

    [Fact]
    public void A_holster_sent_to_somebody_joining_is_named()
        => Assert.Equal("Holster: told Sadriel that Mobile (entity 481) is in Kekkis79's slot 1",
            HolsterLog.Resent("Mobile", 481, "Kekkis79", 1, "Sadriel"));

    [Fact]
    public void A_slot_on_a_player_is_named_by_them()
        => Assert.Equal("Kekkis79", HolsterLog.SlotOwner(3, id => id == 3 ? "Kekkis79" : null));

    [Fact]
    public void A_slot_on_a_player_who_has_gone_is_named_by_their_small_id()
        => Assert.Equal("player 3", HolsterLog.SlotOwner(3, _ => null));

    [Fact]
    public void A_slot_on_a_prop_is_named_by_its_entity()
        => Assert.Equal("entity 700", HolsterLog.SlotOwner(700, _ => "nobody should be asked"));
}

public class HolsterForgetTests
{
    [Fact]
    public void A_removed_item_leaves_its_slot()
    {
        var slots = new HolsterSlots();
        slots.Insert(3, 0, 300);

        Assert.Equal(1, slots.ForgetEntity(300));
        Assert.Null(slots.Find(300));
    }

    [Fact]
    public void The_slots_on_a_removed_prop_are_forgotten()
    {
        var slots = new HolsterSlots();
        slots.Insert(700, 0, 300);
        slots.Insert(700, 1, 301);

        Assert.Equal(2, slots.ForgetEntity(700));
        Assert.Equal(0, slots.Count);
    }

    [Fact]
    public void Other_slots_are_left_alone()
    {
        var slots = new HolsterSlots();
        slots.Insert(3, 0, 300);
        slots.Insert(4, 0, 301);

        slots.ForgetEntity(300);

        Assert.Equal(((ushort)4, (byte)0), slots.Find(301));
    }
}

public class HolsterLogGlueTests
{
    [Fact]
    public void Module_messages_say_who_sent_a_holster_change()
        => Assert.Contains("NoteAttachment(sender, ", FusionServerSource.Method("private void HandleModuleMessage("));

    [Fact]
    public void Both_holster_changes_are_logged()
        => Assert.Equal(2, Regex.Matches(FusionServerSource.Method("private void NoteAttachment("), @"LogHolsterChange\(").Count);

    [Fact]
    public void Every_holster_sent_to_somebody_joining_is_logged()
        => Assert.Contains("HolsterLog.Resent(", FusionServerSource.Method("private int SendAttachments("));

    [Fact]
    public void A_removed_prop_is_taken_off_the_holster_and_magazine_books()
    {
        string method = FusionServerSource.Method("private void ForgetAttachments(");

        Assert.Contains("_slotted.ForgetEntity(", method);
        Assert.Contains("_loaded", method);
        Assert.Contains("Entities.Removed += ForgetAttachments;", FusionServerSource.Text());
    }
}
