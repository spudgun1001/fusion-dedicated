using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The two memories behind the holster duplication rule: what a player has just drawn,
/// and which barcodes they are already suspected over.
/// </summary>
public class HolsterDuplicatesTests
{
    private const byte Joel = 3;
    private const string Pistol = "Pack.Spawnable.Pistol";

    private static readonly DateTime Noon = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    [Fact]
    public void The_first_spawn_is_attempt_one_and_the_next_is_two()
    {
        var dupes = new HolsterDuplicates();

        Assert.Equal(1, dupes.Note(Joel, Pistol, 1, Noon, Window).Attempt);
        Assert.Equal(2, dupes.Note(Joel, Pistol, 1, Noon, Window).Attempt);
        Assert.Equal(3, dupes.Note(Joel, Pistol, 1, Noon, Window).Attempt);
    }

    [Fact]
    public void A_pair_stays_armed_without_a_slot_to_match()
    {
        var dupes = new HolsterDuplicates();
        dupes.Note(Joel, Pistol, 1, Noon, Window);

        Assert.True(dupes.Armed(Joel, Pistol, Noon.AddSeconds(9), Window));
        Assert.Equal(2, dupes.Note(Joel, Pistol, null, Noon.AddSeconds(9), Window).Attempt);
    }

    [Fact]
    public void The_slot_it_was_armed_from_is_kept_for_the_log()
    {
        var dupes = new HolsterDuplicates();
        dupes.Note(Joel, Pistol, 4, Noon, Window);

        Assert.Equal(4, dupes.Note(Joel, Pistol, null, Noon.AddSeconds(1), Window).Slot);
    }

    [Fact]
    public void A_suspicion_lapses_once_the_player_stops_for_the_window()
    {
        var dupes = new HolsterDuplicates();
        dupes.Note(Joel, Pistol, 1, Noon, Window);

        Assert.False(dupes.Armed(Joel, Pistol, Noon.AddSeconds(11), Window));
        Assert.Equal(1, dupes.Note(Joel, Pistol, 1, Noon.AddSeconds(11), Window).Attempt);
    }

    [Fact]
    public void Every_attempt_arms_it_again()
    {
        var dupes = new HolsterDuplicates();
        dupes.Note(Joel, Pistol, 1, Noon, Window);
        dupes.Note(Joel, Pistol, 1, Noon.AddSeconds(9), Window);

        Assert.True(dupes.Armed(Joel, Pistol, Noon.AddSeconds(18), Window));
    }

    [Fact]
    public void A_barcode_in_another_case_is_the_same_barcode()
    {
        var dupes = new HolsterDuplicates();
        dupes.Note(Joel, Pistol, 1, Noon, Window);

        Assert.True(dupes.Armed(Joel, Pistol.ToUpperInvariant(), Noon, Window));
        Assert.Equal(2, dupes.Note(Joel, Pistol.ToUpperInvariant(), 1, Noon, Window).Attempt);
    }

    [Fact]
    public void A_draw_is_found_in_another_case_too()
    {
        var dupes = new HolsterDuplicates();
        dupes.Drew(Joel, Pistol, 2, Noon);

        Assert.Equal((byte)2, dupes.DrawnFrom(Joel, Pistol.ToUpperInvariant(), Noon, Window));
    }

    [Fact]
    public void A_draw_is_forgotten_after_its_window()
    {
        var dupes = new HolsterDuplicates();
        dupes.Drew(Joel, Pistol, 2, Noon);

        Assert.Null(dupes.DrawnFrom(Joel, Pistol, Noon.AddSeconds(4), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void Past_the_cap_a_new_barcode_is_counted_but_not_kept()
    {
        var dupes = new HolsterDuplicates();

        for (int i = 0; i < HolsterDuplicates.MaxRemembered; i++)
        {
            dupes.Note(Joel, $"Pack.Spawnable.Item{i}", 1, Noon, Window);
        }

        // Nothing is refused on the back of a barcode there was no room to keep, which
        // is the safe way for a bounded memory to fail.
        Assert.Equal(1, dupes.Note(Joel, "Pack.Spawnable.Spare", 1, Noon, Window).Attempt);
        Assert.Equal(1, dupes.Note(Joel, "Pack.Spawnable.Spare", 1, Noon, Window).Attempt);
        Assert.False(dupes.Armed(Joel, "Pack.Spawnable.Spare", Noon, Window));
    }

    [Fact]
    public void Past_the_cap_the_oldest_draw_goes()
    {
        var dupes = new HolsterDuplicates();
        dupes.Drew(Joel, Pistol, 1, Noon);

        for (int i = 0; i < HolsterDuplicates.MaxRemembered; i++)
        {
            dupes.Drew(Joel, $"Pack.Spawnable.Item{i}", 2, Noon);
        }

        Assert.Null(dupes.DrawnFrom(Joel, Pistol, Noon, Window));
        Assert.NotNull(dupes.DrawnFrom(Joel, "Pack.Spawnable.Item15", Noon, Window));
    }

    [Fact]
    public void Holstering_it_again_drops_the_draw_but_not_the_suspicion()
    {
        var dupes = new HolsterDuplicates();
        dupes.Drew(Joel, Pistol, 1, Noon);
        dupes.Note(Joel, Pistol, 1, Noon, Window);

        dupes.Holstered(Joel, Pistol);

        Assert.Null(dupes.DrawnFrom(Joel, Pistol, Noon, Window));
        Assert.True(dupes.Armed(Joel, Pistol, Noon, Window));
    }

    [Fact]
    public void A_player_who_leaves_is_forgotten()
    {
        var dupes = new HolsterDuplicates();
        dupes.Drew(Joel, Pistol, 1, Noon);
        dupes.Note(Joel, Pistol, 1, Noon, Window);

        dupes.Forget(Joel);

        Assert.False(dupes.Armed(Joel, Pistol, Noon, Window));
        Assert.Null(dupes.DrawnFrom(Joel, Pistol, Noon, Window));
    }

    [Fact]
    public void A_level_change_forgets_everybody()
    {
        var dupes = new HolsterDuplicates();
        dupes.Note(Joel, Pistol, 1, Noon, Window);
        dupes.Note(9, Pistol, 1, Noon, Window);

        dupes.Clear();

        Assert.False(dupes.Armed(Joel, Pistol, Noon, Window));
        Assert.False(dupes.Armed(9, Pistol, Noon, Window));
    }

    [Fact]
    public void A_blank_barcode_is_never_remembered()
    {
        var dupes = new HolsterDuplicates();

        dupes.Drew(Joel, "  ", 1, Noon);

        Assert.Null(dupes.DrawnFrom(Joel, "  ", Noon, Window));
        Assert.False(dupes.Armed(Joel, "  ", Noon, Window));
    }
}
