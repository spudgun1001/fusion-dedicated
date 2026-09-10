using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Server;
using Steamworks;

namespace FusionDedicated.Tests.Plugins;

/// <summary>What a plugin is told about a connected player, including where they stand.</summary>
public class PluginPlayerSnapshotTests
{
    private static ConnectedPlayer Player() => new()
    {
        Connection = new HSteamNetConnection(1),
        PlatformId = 76561198000000001,
        SmallId = 3,
        Username = "Kanzaaa",
    };

    [Fact]
    public void A_player_is_given_to_plugins_where_they_were_last_seen()
    {
        var player = Player();
        player.LastPosition = new Vec3(1f, 2f, 3f);
        player.HasPosition = true;

        var seen = PluginPlayers.Snapshot(player);

        Assert.True(seen.HasPosition);
        Assert.Equal(1f, seen.X);
        Assert.Equal(2f, seen.Y);
        Assert.Equal(3f, seen.Z);
    }

    [Fact]
    public void A_player_with_no_pose_yet_has_no_position()
        => Assert.False(PluginPlayers.Snapshot(Player()).HasPosition);

    [Fact]
    public void The_snapshot_still_says_who_they_are()
    {
        var player = Player();

        var seen = PluginPlayers.Snapshot(player);

        Assert.Equal(76561198000000001UL, seen.PlatformId);
        Assert.Equal((byte)3, seen.SmallId);
        Assert.Equal(player.DisplayName, seen.Name);
        Assert.Equal(player.Permission, seen.Rank);
    }
}
