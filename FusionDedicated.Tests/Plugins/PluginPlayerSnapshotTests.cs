using BonelabServerBrowser.Fusion;
using FusionDedicated.Plugins;
using FusionDedicated.Server;
using FusionDedicated.Tests.Harness;
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
    public void A_player_is_given_to_plugins_moving_as_fast_as_they_last_were()
    {
        var player = Player();
        player.LastVelocity = new Vec3(4f, 5f, 6f);

        var seen = PluginPlayers.Snapshot(player);

        Assert.Equal(4f, seen.VelocityX);
        Assert.Equal(5f, seen.VelocityY);
        Assert.Equal(6f, seen.VelocityZ);
    }

    [Fact]
    public void A_fast_pose_shows_up_as_velocity_on_the_plugin_player()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        joel.Send(FusionProtocol.BuildPlayerPoseUpdate(joel.SmallId,
            new FusionRigPose { PelvisVelocity = new Vec3(40f, 0f, 0f) }));

        var seen = PluginPlayers.Snapshot(world.Server.Players.Get(joel.SmallId)!);

        Assert.Equal(40f, seen.VelocityX, 0.01f);
        Assert.Equal(0f, seen.VelocityY, 0.01f);
        Assert.Equal(0f, seen.VelocityZ, 0.01f);
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
