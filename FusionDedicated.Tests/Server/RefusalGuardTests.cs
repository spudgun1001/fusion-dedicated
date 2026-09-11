using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// A modded client sent 3,400 refused spawns a second. A log line for each held up the
/// message loop and every player desynced, so refusals are summarised and a flooder is kicked.
/// </summary>
public class RefusalGuardTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    private static RefusalGuard Guard(int kickPerSecond = 50) => new(kickPerSecond, TimeSpan.FromSeconds(5));

    [Fact]
    public void The_first_refusal_is_logged()
        => Assert.True(Guard().Note(3, "spawn", Now).Log);

    [Fact]
    public void Repeats_inside_the_window_are_not_logged()
    {
        var guard = Guard();
        guard.Note(3, "spawn", Now);

        Assert.False(guard.Note(3, "spawn", Now.AddSeconds(1)).Log);
    }

    [Fact]
    public void The_next_one_after_the_window_says_how_many_were_held_back()
    {
        var guard = Guard();
        guard.Note(3, "spawn", Now);
        guard.Note(3, "spawn", Now.AddSeconds(1));
        guard.Note(3, "spawn", Now.AddSeconds(2));

        var verdict = guard.Note(3, "spawn", Now.AddSeconds(6));

        Assert.True(verdict.Log);
        Assert.Equal(2, verdict.Suppressed);
    }

    [Fact]
    public void A_quiet_window_is_summarised()
    {
        var guard = Guard();
        guard.Note(3, "spawn", Now);
        guard.Note(3, "spawn", Now.AddSeconds(1));

        Assert.Equal(new[] { ((byte)3, "spawn", 1) }, guard.Flush(Now.AddSeconds(6)));
    }

    [Fact]
    public void A_window_still_open_is_not_summarised_yet()
    {
        var guard = Guard();
        guard.Note(3, "spawn", Now);
        guard.Note(3, "spawn", Now.AddSeconds(1));

        Assert.Empty(guard.Flush(Now.AddSeconds(2)));
    }

    [Fact]
    public void A_summarised_window_is_not_summarised_twice()
    {
        var guard = Guard();
        guard.Note(3, "spawn", Now);
        guard.Note(3, "spawn", Now.AddSeconds(1));
        guard.Flush(Now.AddSeconds(6));

        Assert.Empty(guard.Flush(Now.AddSeconds(12)));
    }

    [Fact]
    public void A_window_with_nothing_held_back_is_not_summarised()
    {
        var guard = Guard();
        guard.Note(3, "spawn", Now);

        Assert.Empty(guard.Flush(Now.AddSeconds(6)));
    }

    [Fact]
    public void Different_players_and_kinds_are_counted_apart()
    {
        var guard = Guard();
        guard.Note(3, "spawn", Now);

        Assert.True(guard.Note(4, "spawn", Now).Log);
        Assert.True(guard.Note(3, "despawn", Now).Log);
    }

    [Fact]
    public void Flooding_past_the_limit_in_a_second_kicks_once()
    {
        var guard = Guard(kickPerSecond: 50);

        var verdicts = Enumerable.Range(0, 60).Select(i => guard.Note(3, "spawn", Now.AddMilliseconds(i))).ToList();

        Assert.Equal(1, verdicts.Count(v => v.Kick));
        Assert.True(verdicts[50].Kick);
    }

    [Fact]
    public void Refusals_spread_over_seconds_do_not_kick()
    {
        var guard = Guard(kickPerSecond: 50);

        var verdicts = Enumerable.Range(0, 200).Select(i => guard.Note(3, "spawn", Now.AddMilliseconds(i * 100))).ToList();

        Assert.DoesNotContain(verdicts, v => v.Kick);
    }

    [Fact]
    public void A_limit_of_zero_never_kicks()
    {
        var guard = Guard(kickPerSecond: 0);

        var verdicts = Enumerable.Range(0, 1000).Select(_ => guard.Note(3, "spawn", Now)).ToList();

        Assert.DoesNotContain(verdicts, v => v.Kick);
    }

    [Fact]
    public void Every_kind_adds_up_towards_the_kick()
    {
        var guard = Guard(kickPerSecond: 10);

        for (int i = 0; i < 6; i++)
        {
            guard.Note(3, "spawn", Now);
        }

        for (int i = 0; i < 4; i++)
        {
            guard.Note(3, "metadata", Now);
        }

        Assert.True(guard.Note(3, "despawn", Now).Kick);
    }

    [Fact]
    public void A_player_who_left_starts_clean()
    {
        var guard = Guard(kickPerSecond: 50);

        for (int i = 0; i < 60; i++)
        {
            guard.Note(3, "spawn", Now);
        }

        guard.Forget(3);
        var verdict = guard.Note(3, "spawn", Now);

        Assert.True(verdict.Log);
        Assert.False(verdict.Kick);
    }

    [Fact]
    public void Kicking_a_flooder_is_on_by_default()
        => Assert.Equal(50, new ServerConfig().RefusalKickPerSecond);
}

public class RefusalGuardGlueTests
{
    [Fact]
    public void The_spawn_rate_cap_comes_before_every_other_spawn_check()
    {
        string method = FusionServerSource.Method("private void HandleSpawnRequest(");
        int cap = method.IndexOf("_rateLimiter.Allow(", StringComparison.Ordinal);

        Assert.True(cap > 0, "no rate cap");
        Assert.True(cap < method.IndexOf("SpawnAuthority.Check(", StringComparison.Ordinal));
    }

    [Fact]
    public void Spawn_refusals_go_through_the_guard()
    {
        string method = FusionServerSource.Method("private void HandleSpawnRequest(");

        Assert.DoesNotContain("Log(\"WARN\", $\"Spawn", method);
        Assert.Contains("Refuse(sender, \"spawn\"", method);
    }

    [Fact]
    public void Metadata_and_despawn_refusals_go_through_the_guard()
    {
        Assert.Contains("Refuse(sender, \"metadata\"", FusionServerSource.Method("private void HandleMetadataRequest("));
        Assert.Contains("Refuse(sender, \"despawn\"", FusionServerSource.Method("private void HandleDespawnRequest("));
    }

    [Fact]
    public void Held_back_refusals_are_summarised_on_the_tick()
        => Assert.Contains("_refusals.Flush(", FusionServerSource.Method("public void Tick("));

    [Fact]
    public void A_flooder_is_kicked()
        => Assert.Contains("Kick(", FusionServerSource.Method("private void Refuse("));

    [Fact]
    public void Leaving_forgets_a_players_refusals()
        => Assert.Contains("_refusals.Forget(player.SmallId)", FusionServerSource.Method("private void Depart("));
}
