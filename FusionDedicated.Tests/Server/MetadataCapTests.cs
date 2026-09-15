using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Server;

/// <summary>Whether a key was kept, and the server's own keys landing past the client's limit.</summary>
public class MetadataCapTests
{
    private static ConnectedPlayer Player() => new()
    {
        Connection = new HSteamNetConnection(1),
        PlatformId = 76561198000000001,
        SmallId = 3,
        Username = "Kanzaaa",
    };

    private static ConnectedPlayer Full()
    {
        var player = Player();

        for (int i = 0; i < 64; i++)
        {
            player.SetMetadata($"key{i}", "value");
        }

        return player;
    }

    [Fact]
    public void A_key_that_is_kept_says_so()
        => Assert.True(Player().SetMetadata("Nickname", "Kanza"));

    [Fact]
    public void A_new_key_past_the_limit_says_it_was_not_kept()
    {
        var player = Full();

        Assert.False(player.SetMetadata("key64", "value"));
        Assert.Equal(64, player.Metadata.Count);
    }

    [Fact]
    public void A_key_already_held_at_the_limit_says_it_was_kept()
        => Assert.True(Full().SetMetadata("key0", "changed"));

    [Fact]
    public void An_enormous_value_says_it_was_not_kept()
        => Assert.False(Player().SetMetadata("Nickname", new string('v', 5000)));

    [Fact]
    public void The_servers_own_key_is_kept_past_the_limit()
    {
        var player = Full();

        player.SetServerMetadata("PermissionLevel", "OWNER");

        Assert.Equal("OWNER", player.Metadata["PermissionLevel"]);
        Assert.Equal(65, player.Metadata.Count);
    }

    [Fact]
    public void Holding_a_value_is_an_exact_match()
    {
        var player = Player();
        player.SetMetadata("Loading", "False");

        Assert.True(player.HoldsMetadata("Loading", "False"));
        Assert.False(player.HoldsMetadata("Loading", "false"));
        Assert.False(player.HoldsMetadata("Nickname", ""));
    }
}
