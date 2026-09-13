using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A constraint replayed to a joiner. A rig is registered under its player's SmallID
/// and SmallIDs are reused, so a constraint left behind by a player who left lands on
/// whoever is given their id next.
/// </summary>
public class ConstraintCatchupTests
{
    private static World NewWorld() => new(new ServerConfig { CullOrphanedEntities = false });

    private static void Constrain(FakePlayer by, EndSpec first, EndSpec second)
        => by.Send(ModuleProtocol.WriteModuleToClients(ModuleProtocol.ConstraintCreateTag, by.SmallId,
            ConstraintPayloads.Build(by.SmallId, first, second)));

    private static int ReplaysTo(World world, FakePlayer player)
        => world.Transport.SentTo(player.Connection)
            .Count(s => ModuleProtocol.TryReadHandlerTag(s.Message) == ModuleProtocol.ConstraintCreateTag);

    [Fact]
    public void A_joiner_given_a_departed_tied_players_small_id_is_not_sent_their_constraint()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        Constrain(joel, new EntityEnd(kanza.SmallId, 5), new EntityEnd(kanza.SmallId, 9));
        world.Leave(kanza, "Closing Connection");
        Assert.DoesNotContain(world.Server.Entities.Entities, e => e.Synthetic);

        var newbie = world.Join(76561198000000003, "Newbie");

        Assert.Equal(kanza.SmallId, newbie.SmallId);
        Assert.Equal(0, ReplaysTo(world, newbie));
        Assert.DoesNotContain(world.Server.Entities.Entities, e => e.Synthetic);
    }

    [Fact]
    public void A_joiner_is_not_sent_a_constraint_on_a_prop_that_has_gone()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);

        Constrain(joel, new EntityEnd(300), new SceneEnd("/Level/Wall"));
        Assert.True(world.Server.DespawnEntity(300));
        Assert.DoesNotContain(world.Server.Entities.Entities, e => e.Synthetic);

        var newbie = world.Join(76561198000000003, "Newbie");

        Assert.Equal(0, ReplaysTo(world, newbie));
        Assert.DoesNotContain(world.Server.Entities.Entities, e => e.Synthetic);
    }

    [Fact]
    public void A_constraint_whose_end_is_gone_leaves_no_end_behind()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        Constrain(joel, new EntityEnd(joel.SmallId, 5), new SceneEnd("/Level/Wall"));
        var ends = world.Server.Entities.Entities.Where(e => e.Synthetic).Select(e => e.Id).Order().ToList();
        Assert.Equal(2, ends.Count);

        world.Server.Entities.Remove(ends[0]);

        var newbie = world.Join(76561198000000003, "Newbie");

        Assert.DoesNotContain(world.Server.Entities.Entities, e => e.Synthetic);
        Assert.Equal(0, ReplaysTo(world, newbie));
    }

    [Fact]
    public void While_the_tied_player_is_still_here_a_joiner_is_sent_the_constraint()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        Constrain(joel, new EntityEnd(kanza.SmallId, 5), new EntityEnd(kanza.SmallId, 9));

        var newbie = world.Join(76561198000000003, "Newbie");

        Assert.Equal(1, ReplaysTo(world, newbie));
    }

    [Fact]
    public void A_constraint_outlives_the_player_who_made_it()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        Constrain(joel, new EntityEnd(kanza.SmallId, 5), new EntityEnd(kanza.SmallId, 9));
        world.Leave(joel, "Closing Connection");

        var newbie = world.Join(76561198000000003, "Newbie");

        Assert.Equal(1, ReplaysTo(world, newbie));
    }

    [Fact]
    public void A_constraint_to_a_wall_is_still_sent()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        Constrain(joel, new EntityEnd(joel.SmallId, 5), new SceneEnd("/Level/Wall"));

        var newbie = world.Join(76561198000000003, "Newbie");

        Assert.Equal(1, ReplaysTo(world, newbie));
    }

    [Fact]
    public void An_unreadable_constraint_is_still_sent()
    {
        using var world = NewWorld();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        joel.Send(ModuleProtocol.WriteModuleToClients(ModuleProtocol.ConstraintCreateTag, joel.SmallId, new byte[4]));

        var newbie = world.Join(76561198000000003, "Newbie");

        Assert.Equal(1, ReplaysTo(world, newbie));
    }
}
