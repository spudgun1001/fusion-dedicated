using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A join adopted every ownerless prop to one player, 116 at once at 20:01. They stay
/// unowned now, as a leaver's do, and a grab claims one.
/// </summary>
public class JoinLeavesUnownedTests
{
    private static int OwnershipResponses(World world, FakePlayer player, ushort entity)
        => world.Transport.SentTo(player.Connection).Count(sent => sent.Message[0] == FusionProtocol.TagEntityOwnershipResponse
            && FusionProtocol.TryReadOwnershipResponse(sent.Message) is { } r && r.EntityId == entity);

    [Fact]
    public void A_loose_ownerless_prop_stays_unowned_when_somebody_joins()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 0, 0, 0);
        world.Server.Entities.SetOwner(300, null);
        int before = OwnershipResponses(world, joel, 300);

        var newbie = world.Join(76561198000000002, "Newbie");
        newbie.FinishLoading();

        Assert.Null(world.Server.Entities.Get(300)!.OwnerSmallId);
        Assert.Equal(before, OwnershipResponses(world, joel, 300));
        // Only the joiner is pointed at a settled player, whose pose a game takes only from the owner it knows.
        Assert.Equal(joel.SmallId, newbie.View.Entities[300].Owner);
    }

    [Fact]
    public void An_ownerless_prop_somebody_holds_is_still_adopted()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 0, 0, 0);
        joel.Grab(300);
        world.Server.Entities.SetOwner(300, null);

        world.Join(76561198000000002, "Newbie");

        Assert.NotNull(world.Server.Entities.Get(300)!.OwnerSmallId);
    }
}
