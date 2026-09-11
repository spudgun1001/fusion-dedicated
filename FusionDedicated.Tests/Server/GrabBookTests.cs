using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// What each player is holding. The join catch-up and the ownership rules both
/// need to know whether a gun is in somebody's hand.
/// </summary>
public class GrabBookTests
{
    private const byte Left = 1;
    private const byte Right = 2;
    private const ushort Gun = 300;

    [Fact]
    public void A_grab_is_remembered()
    {
        var book = new GrabBook();

        book.Grab(3, Right, Gun);

        Assert.Equal(new byte[] { 3 }, book.HoldersOf(Gun));
        Assert.Equal(new HeldItem(3, Right, Gun), Assert.Single(book.All()));
    }

    [Fact]
    public void Letting_go_forgets_it()
    {
        var book = new GrabBook();
        book.Grab(3, Right, Gun);

        book.Release(3, Right);

        Assert.Empty(book.HoldersOf(Gun));
    }

    [Fact]
    public void Letting_go_with_the_other_hand_changes_nothing()
    {
        var book = new GrabBook();
        book.Grab(3, Right, Gun);

        book.Release(3, Left);

        Assert.Equal(new byte[] { 3 }, book.HoldersOf(Gun));
    }

    [Fact]
    public void A_new_grab_on_a_hand_replaces_what_it_held()
    {
        var book = new GrabBook();
        book.Grab(3, Right, Gun);

        book.Grab(3, Right, 301);

        Assert.Empty(book.HoldersOf(Gun));
        Assert.Equal(new byte[] { 3 }, book.HoldersOf(301));
    }

    [Fact]
    public void Two_hands_on_one_gun_name_the_player_once()
    {
        var book = new GrabBook();

        book.Grab(3, Left, Gun);
        book.Grab(3, Right, Gun);

        Assert.Equal(new byte[] { 3 }, book.HoldersOf(Gun));
        Assert.Equal(2, book.All().Count);
    }

    [Fact]
    public void Holders_are_listed_in_the_order_they_grabbed()
    {
        var book = new GrabBook();

        book.Grab(5, Left, Gun);
        book.Grab(3, Right, Gun);

        Assert.Equal(new byte[] { 5, 3 }, book.HoldersOf(Gun));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(255)]
    public void Only_the_left_and_right_hands_are_kept(byte hand)
    {
        var book = new GrabBook();

        book.Grab(3, hand, Gun);

        Assert.Empty(book.All());
    }

    [Fact]
    public void A_player_who_goes_holds_nothing()
    {
        var book = new GrabBook();
        book.Grab(3, Left, Gun);
        book.Grab(3, Right, 301);
        book.Grab(4, Left, Gun);

        Assert.Equal(2, book.ForgetPlayer(3));
        Assert.Equal(new byte[] { 4 }, book.HoldersOf(Gun));
        Assert.Empty(book.HoldersOf(301));
    }

    [Fact]
    public void An_entity_that_goes_is_in_nobodys_hand()
    {
        var book = new GrabBook();
        book.Grab(3, Left, Gun);
        book.Grab(4, Right, Gun);
        book.Grab(4, Left, 301);

        Assert.Equal(2, book.ForgetEntity(Gun));
        Assert.Equal(new HeldItem(4, Left, 301), Assert.Single(book.All()));
    }

    [Fact]
    public void Nothing_held_names_nobody()
        => Assert.Empty(new GrabBook().HoldersOf(Gun));

    [Fact]
    public void Reading_while_it_changes_does_not_throw()
    {
        // Written on the message loop and read from plugins and the panel.
        var book = new GrabBook();

        Parallel.For(0, 5000, i =>
        {
            byte player = (byte)(i % 20 + 1);
            byte hand = (byte)(i % 2 + 1);
            ushort entity = (ushort)(300 + i % 7);

            book.Grab(player, hand, entity);
            book.HoldersOf(entity);
            book.All();

            if (i % 3 == 0)
            {
                book.Release(player, hand);
            }
        });
    }
}
