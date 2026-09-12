using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

public class WorldTests
{
    [Fact]
    public void A_late_joiner_learns_who_owns_a_prop()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Server.Entities.Register(300, "Pack.Spawnable.Crate", joel.SmallId, 1, 2, 3);

        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        Assert.Equal(joel.SmallId, kanza.View.Entities[300].Owner);
    }

    [Fact]
    public void Moving_the_clock_runs_the_resends_after_loading()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Server.Entities.Register(301, "Pack.Spawnable.Gun", joel.SmallId, 0, 0, 0);
        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, 301, 0));

        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        int ModuleMessages() => world.Transport.SentTo(kanza.Connection).Count(m => m.Message[0] == ModuleProtocol.TagModule);
        int before = ModuleMessages();

        world.Advance(TimeSpan.FromSeconds(7));

        Assert.True(ModuleMessages() > before, "no holster was sent again after loading");
    }

    [Fact]
    public void A_player_sees_its_own_holster_like_everyone_else()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();
        world.Server.Entities.Register(301, "Pack.Spawnable.Gun", joel.SmallId, 0, 0, 0);

        joel.Send(ClientMessages.SlotInsert(joel.SmallId, joel.SmallId, 301, 0));

        Assert.Equal((ushort)301, joel.View.Slots[(joel.SmallId, 0)]);
        Assert.Equal((ushort)301, kanza.View.Slots[(joel.SmallId, 0)]);
        Assert.True(world.SlotsAgree(), "holster slots differ between players");
    }

    [Fact]
    public void A_player_who_leaves_is_gone_from_the_server()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");

        world.Leave(joel, "Closing Connection");

        Assert.Null(world.Server.Players.GetByPlatformId(76561198000000001));
    }

    [Fact]
    public void A_prop_spawned_while_players_are_here_is_seen_by_them_all()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(kanza, 300, "Pack.Spawnable.Crate", 1, 2, 3);

        Assert.Equal(kanza.SmallId, joel.View.Entities[300].Owner);
        Assert.Equal(kanza.SmallId, kanza.View.Entities[300].Owner);
    }

    [Fact]
    public void Building_someone_elses_prop_asks_them_for_its_state()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(76561198000000002, "Kanza");
        kanza.FinishLoading();

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);

        Assert.Contains(world.Transport.SentTo(joel.Connection), sent =>
            Envelope.Read(sent.Message) is { } envelope
            && envelope.Tag == FusionProtocol.TagEntityDataRequest
            && envelope.Sender == kanza.SmallId);

        Assert.DoesNotContain(world.Transport.SentTo(kanza.Connection), sent =>
            Envelope.Read(sent.Message) is { } envelope
            && envelope.Tag == FusionProtocol.TagEntityDataRequest
            && envelope.Sender == joel.SmallId);
    }
}
