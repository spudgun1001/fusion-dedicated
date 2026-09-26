using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Holsters and magazines were re-seated two or four times a join, 40 to 59 each time.
/// One pass now, once the game has loaded.
/// </summary>
public class SingleReseatTests
{
    private static (World World, FakePlayer Joel) Build()
    {
        var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Spawn(joel, 300, "Pack.Spawnable.Gun", 0, 0, 0);
        world.Spawn(joel, 301, "Pack.Spawnable.MagGun", 0, 0, 0);
        world.Spawn(joel, 302, "Pack.Spawnable.Gun", 0, 0, 0);
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, 300, 1));
        joel.Send(ClientMessages.MagazineInsert(joel.SmallId, 301, 302));

        return (world, joel);
    }

    private static int Reseats(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection).Count(sent =>
            ModuleProtocol.TryReadHandlerTag(sent.Message) is { } tag
            && (tag == ModuleProtocol.InventorySlotInsertTag || tag == ModuleProtocol.MagazineInsertTag));

    private static void Wait(World world, int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            world.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void A_player_who_loads_after_joining_gets_one_pass()
    {
        var (world, _) = Build();
        using var __ = world;
        var newbie = world.Join(76561198000000002, "Newbie");
        newbie.Send(ClientMessages.Metadata(newbie.SmallId, "Loading", "True"));

        Wait(world, 30);
        newbie.FinishLoading();
        Wait(world, 30);

        Assert.Equal(2, Reseats(world, newbie));
    }

    [Fact]
    public void A_player_who_never_says_it_is_loading_gets_one_pass()
    {
        var (world, _) = Build();
        using var __ = world;
        var newbie = world.Join(76561198000000002, "Newbie");

        Wait(world, 30);

        Assert.Equal(2, Reseats(world, newbie));
    }

    [Fact]
    public void A_player_already_sent_one_before_it_said_it_was_loading_gets_it_again_once_loaded()
    {
        var (world, _) = Build();
        using var __ = world;
        var newbie = world.Join(76561198000000002, "Newbie");

        Wait(world, 5);
        newbie.Send(ClientMessages.Metadata(newbie.SmallId, "Loading", "True"));
        Wait(world, 20);
        newbie.FinishLoading();
        Wait(world, 30);

        Assert.Equal(4, Reseats(world, newbie));
    }
}
