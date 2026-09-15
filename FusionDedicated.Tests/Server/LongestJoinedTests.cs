using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

public class LongestJoinedTests
{
    private static readonly DateTime Noon = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static ConnectedPlayer Player(byte smallId, DateTime joinedAt) => new()
    {
        Connection = new Steamworks.HSteamNetConnection(smallId),
        PlatformId = 76561198000000000UL + smallId,
        SmallId = smallId,
        JoinedAt = joinedAt,
    };

    [Fact]
    public void The_longest_joined_player_is_chosen_whatever_their_small_id()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, Noon));
        registry.Add(Player(2, Noon.AddMinutes(-10)));

        Assert.Equal((byte?)2, registry.LongestJoined()?.SmallId);
    }

    [Fact]
    public void A_tie_goes_to_the_lower_small_id()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(2, Noon));
        registry.Add(Player(1, Noon));

        Assert.Equal((byte?)1, registry.LongestJoined()?.SmallId);
    }

    [Fact]
    public void The_named_player_is_left_out()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, Noon.AddMinutes(-10)));
        registry.Add(Player(2, Noon));

        Assert.Equal((byte?)2, registry.LongestJoined(except: 1)?.SmallId);
    }

    [Fact]
    public void Nobody_here_gives_nobody()
        => Assert.Null(new PlayerRegistry().LongestJoined());
}
