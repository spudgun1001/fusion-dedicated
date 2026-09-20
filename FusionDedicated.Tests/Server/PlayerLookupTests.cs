using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// The two lookups the message loop makes on every message a player sends.
///
/// Both used to walk the register, and one of them sorted and copied it, which at
/// fifty players was most of what handling a message allocated.
/// </summary>
public class PlayerLookupTests
{
    private static ConnectedPlayer Player(byte smallId, uint connection = 0)
        => new()
        {
            SmallId = smallId,
            PlatformId = 76561198000000000UL + smallId,
            Connection = new HSteamNetConnection(connection == 0 ? 1000u + smallId : connection),
        };

    [Fact]
    public void The_player_list_is_the_same_list_until_somebody_joins_or_leaves()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1));
        registry.Add(Player(2));

        var first = registry.Players;

        Assert.Same(first, registry.Players);

        registry.Add(Player(3));

        Assert.NotSame(first, registry.Players);
        Assert.Equal(3, registry.Players.Count);
    }

    [Fact]
    public void The_player_list_is_rebuilt_when_somebody_leaves()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1));
        registry.Add(Player(2));

        var before = registry.Players;
        registry.RemoveBySmallId(2);

        Assert.NotSame(before, registry.Players);
        Assert.Equal(new byte[] { 1 }, registry.Players.Select(p => p.SmallId).ToArray());
    }

    [Fact]
    public void The_player_list_stays_in_small_id_order()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(7));
        registry.Add(Player(2));
        registry.Add(Player(5));

        Assert.Equal(new byte[] { 2, 5, 7 }, registry.Players.Select(p => p.SmallId).ToArray());
    }

    [Fact]
    public void A_player_is_found_by_their_connection()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, connection: 44));
        registry.Add(Player(2, connection: 45));

        Assert.Equal((byte)2, registry.GetByConnection(new HSteamNetConnection(45))?.SmallId);
        Assert.Null(registry.GetByConnection(new HSteamNetConnection(46)));
    }

    [Fact]
    public void A_connection_is_forgotten_however_the_player_was_removed()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(1, connection: 44));
        registry.Add(Player(2, connection: 45));

        registry.Remove(new HSteamNetConnection(44));
        registry.RemoveBySmallId(2);

        Assert.Null(registry.GetByConnection(new HSteamNetConnection(44)));
        Assert.Null(registry.GetByConnection(new HSteamNetConnection(45)));
    }

    [Fact]
    public void A_reused_connection_handle_finds_whoever_holds_it_now()
    {
        // Steam hands the same handle out again once the old one has gone.
        var registry = new PlayerRegistry();
        registry.Add(Player(1, connection: 44));
        registry.RemoveBySmallId(1);
        registry.Add(Player(9, connection: 44));

        Assert.Equal((byte)9, registry.GetByConnection(new HSteamNetConnection(44))?.SmallId);
    }

    [Fact]
    public void A_player_is_found_by_their_platform_id()
    {
        var registry = new PlayerRegistry();
        registry.Add(Player(3));

        Assert.Equal((byte)3, registry.GetByPlatformId(76561198000000003UL)?.SmallId);
        Assert.Null(registry.GetByPlatformId(1UL));
    }
}
