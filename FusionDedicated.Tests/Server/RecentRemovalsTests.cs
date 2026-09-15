using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>Which ids the server removed in the last five seconds, so a resent despawn is not sent to everybody again.</summary>
public class RecentRemovalsTests
{
    private static readonly DateTime Start = new(2026, 9, 15, 19, 14, 31, DateTimeKind.Utc);

    private DateTime _now = Start;

    private RecentRemovals Removals() => new(() => _now);

    [Fact]
    public void An_id_never_removed_is_not_recent()
        => Assert.False(Removals().Contains(300));

    [Fact]
    public void A_removed_id_is_recent_until_just_before_five_seconds()
    {
        var removals = Removals();
        removals.Note(300);

        Assert.True(removals.Contains(300));

        _now = Start.AddMilliseconds(4999);

        Assert.True(removals.Contains(300));
    }

    [Fact]
    public void A_removed_id_is_no_longer_recent_at_five_seconds()
    {
        var removals = Removals();
        removals.Note(300);

        _now = Start.AddSeconds(5);

        Assert.False(removals.Contains(300));
    }

    [Fact]
    public void Each_id_keeps_its_own_time()
    {
        var removals = Removals();
        removals.Note(300);

        _now = Start.AddSeconds(3);
        removals.Note(301);

        _now = Start.AddSeconds(5);

        Assert.False(removals.Contains(300));
        Assert.True(removals.Contains(301));
    }

    [Fact]
    public void Removing_an_id_again_starts_its_time_again()
    {
        var removals = Removals();
        removals.Note(300);

        _now = Start.AddSeconds(4);
        removals.Note(300);

        _now = Start.AddSeconds(6);

        Assert.True(removals.Contains(300));
    }

    [Fact]
    public void Old_removals_are_dropped_as_new_ones_come_in()
    {
        var removals = Removals();
        removals.Note(300);
        removals.Note(301);

        _now = Start.AddSeconds(5);
        removals.Note(302);

        Assert.Equal(1, removals.Count);
    }

    [Fact]
    public void Clearing_forgets_every_removal()
    {
        var removals = Removals();
        removals.Note(300);
        removals.Note(301);

        removals.Clear();

        Assert.False(removals.Contains(300));
        Assert.Equal(0, removals.Count);
    }

    [Fact]
    public void Every_removal_is_noted()
        => Assert.Contains("Entities.Removed += id => _recentRemovals.Note(id);", FusionServerSource.Text());

    [Fact]
    public void A_level_change_clears_them_after_the_old_level_is_forgotten()
    {
        string method = FusionServerSource.Method("public void SetLevel(");

        int forget = method.IndexOf("Entities.Forget();", StringComparison.Ordinal);
        int clear = method.IndexOf("_recentRemovals.Clear();", StringComparison.Ordinal);

        Assert.True(clear > 0, "not cleared");
        Assert.True(clear > forget, "forgetting the level notes every entity as removed, so it must be cleared after");
    }

    [Fact]
    public void A_kept_prop_is_refused_before_anybody_is_asked_whether_they_may_despawn()
    {
        string method = FusionServerSource.Method("private void HandleDespawnRequest(");

        int kept = method.IndexOf("Persistent: true", StringComparison.Ordinal);

        Assert.True(kept > 0, "no kept prop check");
        Assert.True(kept < method.IndexOf("DespawnAuthority.MayDespawn(", StringComparison.Ordinal));
        Assert.True(kept < method.IndexOf("Config.ExtendedProtection", StringComparison.Ordinal));
    }
}
