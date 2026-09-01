using FusionDedicated;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

public class SpawnRateLimiterTests
{
    private readonly DateTime _t0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Spawns_under_the_cap_are_allowed()
    {
        var limiter = new SpawnRateLimiter(maxPerSecond: 5);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(limiter.Allow(1, _t0));
        }
    }

    [Fact]
    public void The_spawn_over_the_cap_is_refused()
    {
        var limiter = new SpawnRateLimiter(maxPerSecond: 5);

        for (var i = 0; i < 5; i++)
        {
            limiter.Allow(1, _t0);
        }

        Assert.False(limiter.Allow(1, _t0));
    }

    [Fact]
    public void The_window_slides_so_a_later_spawn_is_allowed()
    {
        var limiter = new SpawnRateLimiter(maxPerSecond: 2);

        limiter.Allow(1, _t0);
        limiter.Allow(1, _t0);

        Assert.False(limiter.Allow(1, _t0.AddMilliseconds(500)));
        Assert.True(limiter.Allow(1, _t0.AddMilliseconds(1100)));
    }

    [Fact]
    public void Players_are_limited_independently()
    {
        var limiter = new SpawnRateLimiter(maxPerSecond: 1);

        Assert.True(limiter.Allow(1, _t0));
        Assert.False(limiter.Allow(1, _t0));
        Assert.True(limiter.Allow(2, _t0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_cap_of_zero_or_less_disables_the_limiter(int cap)
    {
        var limiter = new SpawnRateLimiter(cap);

        for (var i = 0; i < 50; i++)
        {
            Assert.True(limiter.Allow(1, _t0));
        }
    }

    [Fact]
    public void Forgetting_a_player_clears_their_history()
    {
        var limiter = new SpawnRateLimiter(maxPerSecond: 1);

        limiter.Allow(1, _t0);
        limiter.Forget(1);

        Assert.True(limiter.Allow(1, _t0));
    }
}

public class NicknameGuardTests
{
    private readonly DateTime _t0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_reserved_nickname_is_refused()
    {
        var guard = new NicknameGuard(maxChangesPerMinute: 3, new[] { "Admin", "Owner" });

        Assert.False(guard.Allow(1, "admin", _t0).Allowed);
    }

    [Fact]
    public void Reserved_matching_ignores_case_and_spacing()
    {
        var guard = new NicknameGuard(maxChangesPerMinute: 3, new[] { "Admin" });

        Assert.False(guard.Allow(1, "  ADMIN  ", _t0).Allowed);
    }

    [Fact]
    public void An_ordinary_nickname_is_allowed()
    {
        var guard = new NicknameGuard(maxChangesPerMinute: 3, new[] { "Admin" });

        Assert.True(guard.Allow(1, "Spudgun", _t0).Allowed);
    }

    [Fact]
    public void Changing_too_often_is_refused()
    {
        var guard = new NicknameGuard(maxChangesPerMinute: 2, Array.Empty<string>());

        Assert.True(guard.Allow(1, "one", _t0).Allowed);
        Assert.True(guard.Allow(1, "two", _t0).Allowed);
        Assert.False(guard.Allow(1, "three", _t0).Allowed);
    }

    [Fact]
    public void The_change_window_expires()
    {
        var guard = new NicknameGuard(maxChangesPerMinute: 1, Array.Empty<string>());

        guard.Allow(1, "one", _t0);

        Assert.False(guard.Allow(1, "two", _t0.AddSeconds(30)).Allowed);
        Assert.True(guard.Allow(1, "three", _t0.AddSeconds(61)).Allowed);
    }

    [Fact]
    public void A_limit_of_zero_disables_the_rate_rule_but_not_reservations()
    {
        var guard = new NicknameGuard(maxChangesPerMinute: 0, new[] { "Admin" });

        for (var i = 0; i < 20; i++)
        {
            Assert.True(guard.Allow(1, $"name{i}", _t0).Allowed);
        }

        Assert.False(guard.Allow(1, "Admin", _t0).Allowed);
    }
}

public class DespawnAuthorityTests
{
    [Fact]
    public void The_owner_may_despawn_their_own_entity()
    {
        Assert.True(DespawnAuthority.MayDespawn(entityOwner: 5, requester: 5, PermissionLevel.Default));
    }

    [Fact]
    public void A_stranger_may_not_despawn_someone_elses_entity()
    {
        Assert.False(DespawnAuthority.MayDespawn(entityOwner: 5, requester: 9, PermissionLevel.Default));
    }

    [Theory]
    [InlineData(PermissionLevel.Operator)]
    [InlineData(PermissionLevel.Owner)]
    public void An_operator_may_despawn_anything(PermissionLevel rank)
    {
        Assert.True(DespawnAuthority.MayDespawn(entityOwner: 5, requester: 9, rank));
    }

    [Fact]
    public void An_orphaned_entity_may_be_despawned_by_anyone()
    {
        Assert.True(DespawnAuthority.MayDespawn(entityOwner: null, requester: 9, PermissionLevel.Default));
    }
}
