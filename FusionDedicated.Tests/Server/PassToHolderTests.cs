using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// Fusion only takes an item back when the last hand left on it is the same player's.
/// So an owner letting go of something another player still held left it owned by
/// somebody not holding it, and the holder's movement was ignored.
/// </summary>
public class PassToHolderTests
{
    private const byte Left = 1;
    private const byte Right = 2;

    [Fact]
    public void The_owner_letting_go_passes_it_to_whoever_still_holds_it()
        => Assert.Equal((byte)2, WorldCatchup.NextHolder(releasedBy: 1, owner: 1, holders: new byte[] { 2 }));

    [Fact]
    public void The_owner_still_holding_it_in_the_other_hand_keeps_it()
        => Assert.Null(WorldCatchup.NextHolder(releasedBy: 1, owner: 1, holders: new byte[] { 1, 2 }));

    [Fact]
    public void Somebody_else_letting_go_changes_nothing()
        => Assert.Null(WorldCatchup.NextHolder(releasedBy: 2, owner: 1, holders: new byte[] { 1 }));

    [Fact]
    public void Nobody_left_holding_it_changes_nothing()
        => Assert.Null(WorldCatchup.NextHolder(releasedBy: 1, owner: 1, holders: Array.Empty<byte>()));

    [Fact]
    public void Whoever_grabbed_it_first_gets_it()
        => Assert.Equal((byte)3, WorldCatchup.NextHolder(releasedBy: 1, owner: 1, holders: new byte[] { 3, 2 }));

    [Fact]
    public void A_release_says_what_the_hand_held()
    {
        var book = new GrabBook();
        book.Grab(1, Left, 300);

        Assert.Equal((ushort)300, book.Release(1, Left));
    }

    [Fact]
    public void Releasing_an_empty_hand_says_nothing()
        => Assert.Null(new GrabBook().Release(1, Left));

    [Fact]
    public void Grabbing_something_else_says_what_the_hand_let_go_of()
    {
        var book = new GrabBook();
        book.Grab(1, Right, 300);

        Assert.Equal((ushort)300, book.Grab(1, Right, 301));
    }

    [Fact]
    public void Grabbing_the_same_thing_again_lets_go_of_nothing()
    {
        var book = new GrabBook();
        book.Grab(1, Right, 300);

        Assert.Null(book.Grab(1, Right, 300));
    }

    private static string Case(string label)
    {
        string handler = FusionServerSource.Method("private void HandleMessage(");
        int start = handler.IndexOf(label, StringComparison.Ordinal);

        Assert.True(start > 0, $"'{label}' moved");

        int end = handler.IndexOf("\n            case ", start + label.Length, StringComparison.Ordinal);

        return end > start ? handler[start..end] : handler[start..];
    }

    [Fact]
    public void A_release_passes_the_item_on()
        => Assert.Contains("PassToHolder(sender, ", Case("case FusionProtocol.TagPlayerRepRelease when sender != null:"));

    [Fact]
    public void A_grab_that_empties_a_hand_passes_the_item_on()
        => Assert.Contains("PassToHolder(sender, ", FusionServerSource.Method("private void NoteGrab("));

    [Fact]
    public void Passing_on_follows_the_rule_and_tells_everybody()
    {
        string method = FusionServerSource.Method("private void PassToHolder(");

        Assert.Contains("WorldCatchup.NextHolder(", method);
        Assert.Contains("MayHold(", method);
        Assert.Contains("AnnounceOwner(", method);
    }

    [Fact]
    public void A_vehicle_with_its_driver_in_it_is_not_passed_on()
        => Assert.Contains("WorldCatchup.DriverKeeps(", FusionServerSource.Method("private void PassToHolder("));
}
