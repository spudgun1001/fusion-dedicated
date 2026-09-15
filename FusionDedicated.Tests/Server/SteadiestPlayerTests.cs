using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>A game still loading simulates nothing, so owner fallbacks prefer somebody who has loaded.</summary>
public class SteadiestPlayerTests
{
    private static readonly DateTime Noon = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static ConnectedPlayer Player(byte smallId, DateTime joinedAt, bool loaded) => new()
    {
        Connection = new Steamworks.HSteamNetConnection(smallId),
        PlatformId = 76561198000000000UL + smallId,
        SmallId = smallId,
        JoinedAt = joinedAt,
        Loaded = loaded,
    };

    [Fact]
    public void A_loaded_player_is_chosen_over_a_longer_joined_one_still_loading()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, Noon.AddMinutes(-10), loaded: false));
        registry.Add(Player(2, Noon, loaded: true));

        Assert.Equal((byte?)2, registry.SteadiestPlayer()?.SmallId);
    }

    [Fact]
    public void Of_the_loaded_players_the_longest_joined_is_chosen()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, Noon, loaded: true));
        registry.Add(Player(2, Noon.AddMinutes(-10), loaded: true));
        registry.Add(Player(3, Noon.AddMinutes(-20), loaded: false));

        Assert.Equal((byte?)2, registry.SteadiestPlayer()?.SmallId);
    }

    [Fact]
    public void With_nobody_loaded_the_longest_joined_is_chosen()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, Noon, loaded: false));
        registry.Add(Player(2, Noon.AddMinutes(-10), loaded: false));

        Assert.Equal((byte?)2, registry.SteadiestPlayer()?.SmallId);
    }

    [Fact]
    public void The_named_player_is_left_out_even_when_loaded()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, Noon.AddMinutes(-10), loaded: true));
        registry.Add(Player(2, Noon, loaded: false));

        Assert.Equal((byte?)2, registry.SteadiestPlayer(except: 1)?.SmallId);
    }

    [Fact]
    public void Nobody_here_gives_nobody()
        => Assert.Null(new PlayerRegistry().SteadiestPlayer());

    [Fact]
    public void A_new_player_has_not_loaded()
        => Assert.False(new ConnectedPlayer
        {
            Connection = new Steamworks.HSteamNetConnection(1),
            PlatformId = 76561198000000001UL,
            SmallId = 1,
        }.Loaded);
}
