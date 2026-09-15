using FusionDedicated.Server;
using FusionDedicated.Tests.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>Privacy is enforced at join the way Fusion's own host does it, with no exemption for staff.</summary>
public class PrivacyTests
{
    private const ulong FriendId = 76561198000000001;
    private const ulong StrangerId = 76561198000000002;

    [Fact]
    public void Nobody_is_a_friend_until_the_host_says_how_to_tell()
    {
        using var server = new FusionServer(new ServerConfig(), new FakeTransport());

        Assert.False(server.IsFriend(FriendId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Public_and_private_admit_anyone(int privacy)
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, Privacy = privacy });

        world.Join(StrangerId, "Stranger");

        Assert.NotNull(world.Server.Players.GetByPlatformId(StrangerId));
    }

    [Fact]
    public void Friends_only_admits_a_friend()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, Privacy = 2 });
        world.Server.IsFriend = id => id == FriendId;

        world.Join(FriendId, "Friend");

        Assert.NotNull(world.Server.Players.GetByPlatformId(FriendId));
    }

    [Fact]
    public void Friends_only_refuses_a_stranger_and_closes_the_connection()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false, Privacy = 2 });
        world.Server.IsFriend = id => id == FriendId;

        var connection = ConnectionCloseTests.Ask(world, StrangerId, "Stranger");

        Assert.Null(world.Server.Players.GetByPlatformId(StrangerId));
        Assert.Equal("Server is private.", ConnectionCloseTests.RefusalSentTo(world, connection));
        Assert.Contains(world.Server.RecentLog(2000), e => e.Message ==
            "Rejected 76561198000000002: Privacy is Friends only and they are not a Steam friend of the server's account");

        world.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Contains(world.Transport.Closed, c => c.Connection == connection.m_HSteamNetConnection);
    }

    [Fact]
    public void Locked_refuses_everybody_an_owner_included()
    {
        var config = new ServerConfig { CullOrphanedEntities = false, Privacy = 3 };
        config.Permissions.Add(new PermissionEntry { PlatformId = FriendId, Level = PermissionLevel.Owner });
        using var world = new World(config);
        world.Server.IsFriend = _ => true;

        var connection = ConnectionCloseTests.Ask(world, FriendId, "Owner");

        Assert.Null(world.Server.Players.GetByPlatformId(FriendId));
        Assert.Equal("Server is private.", ConnectionCloseTests.RefusalSentTo(world, connection));
        Assert.Contains(world.Server.RecentLog(2000), e => e.Message == "Rejected 76561198000000001: Privacy is Locked");
    }

    [Fact]
    public void The_host_asks_steam_whether_they_are_friends()
    {
        string construction = ProgramSource.Between("new FusionServer(config)", "server.Start();");

        Assert.Contains(
            "IsFriend = id => SteamFriends.GetFriendRelationship(new CSteamID(id)) == EFriendRelationship.k_EFriendRelationshipFriend",
            construction);
    }
}
