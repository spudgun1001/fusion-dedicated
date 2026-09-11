using FusionDedicated.Server;

namespace FusionDedicated.Tests.Server;

/// <summary>Handing one entity to one player, the way the ownership plugin's hand back does.</summary>
public class PluginGiveOwnerTests
{
    private static FusionServer Server() => new(new ServerConfig());

    [Fact]
    public void Giving_an_owner_sets_it_and_announces()
    {
        var server = Server();
        server.Entities.Register(300, "Pack.Spawnable.Thing", 0, 0f, 0f, 0f);

        server.Players.Add(new ConnectedPlayer
        {
            Connection = new Steamworks.HSteamNetConnection(1),
            PlatformId = 76561198000000001,
            SmallId = 2,
        });

        Assert.True(server.GiveOwner(300, 76561198000000001));
        Assert.Equal((byte)2, server.Entities.Get(300)!.OwnerSmallId);
    }

    [Fact]
    public void Giving_an_owner_of_a_missing_entity_fails()
    {
        var server = Server();

        server.Players.Add(new ConnectedPlayer
        {
            Connection = new Steamworks.HSteamNetConnection(1),
            PlatformId = 1,
            SmallId = 2,
        });

        Assert.False(server.GiveOwner(9000, 1));
    }

    [Fact]
    public void Giving_an_owner_to_somebody_not_connected_fails()
    {
        var server = Server();
        server.Entities.Register(300, "Pack.Spawnable.Thing", 0, 0f, 0f, 0f);

        Assert.False(server.GiveOwner(300, 999));
    }
}
