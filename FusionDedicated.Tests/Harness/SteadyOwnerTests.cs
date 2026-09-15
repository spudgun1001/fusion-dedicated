using BonelabServerBrowser.Fusion;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A player still loading is never picked to own something. Their game cannot simulate
/// it, so a prop handed to them hangs for everybody until they load.
/// </summary>
public class SteadyOwnerTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong KanzaId = 76561198000000002;
    private const ulong MiaId = 76561198000000003;
    private const ulong NewbieId = 76561198000000004;

    /// <summary>Joel and Mia have loaded. Kanza, in between them, is still loading.</summary>
    private static (World World, FakePlayer Joel, FakePlayer Kanza, FakePlayer Mia) KanzaStillLoading()
    {
        var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        var mia = world.Join(MiaId, "Mia");
        mia.FinishLoading();

        return (world, joel, kanza, mia);
    }

    [Fact]
    public void A_player_counts_as_loaded_from_saying_so_until_they_load_again()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var player = world.Server.Players.Get(joel.SmallId)!;

        Assert.False(player.Loaded);

        joel.FinishLoading();
        Assert.True(player.Loaded);

        joel.Send(ClientMessages.Metadata(joel.SmallId, "Loading", "True"));
        Assert.False(player.Loaded);
    }

    [Fact]
    public void A_level_change_counts_everybody_as_loading_again()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        world.Server.SetLevel("Pack.Level.Other", "Other", 0, null);

        Assert.False(world.Server.Players.Get(joel.SmallId)!.Loaded);
    }

    [Fact]
    public void A_leavers_props_go_to_a_loaded_player_rather_than_one_still_loading()
    {
        var (world, joel, kanza, mia) = KanzaStillLoading();
        using var _ = world;

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);
        world.Leave(joel, "Closing Connection");

        Assert.Equal((byte?)mia.SmallId, world.Server.Entities.Get(300)!.OwnerSmallId);
    }

    [Fact]
    public void A_scene_prop_whose_owners_have_gone_is_named_for_a_loaded_player()
    {
        var (world, joel, kanza, mia) = KanzaStillLoading();
        using var _ = world;

        var door = world.Server.Entities.Register(700, "", joel.SmallId, 0, 0, 0);
        door.Discovered = true;
        joel.Send(FusionProtocol.BuildPropCreate(joel.SmallId, 1234567, 2, 700));

        // Nobody owns it now and the player who networked it is gone, so the newcomer is told somebody else.
        world.Server.Entities.SetOwner(700, null);
        world.Leave(joel, "Closing Connection");

        var newbie = world.Join(NewbieId, "Newbie");

        var told = world.Transport.SentTo(newbie.Connection)
            .Where(sent => sent.Message[0] == FusionProtocol.TagNetworkPropCreate)
            .Select(sent => FusionProtocol.TryReadPropCreate(sent.Message))
            .Single(prop => prop != null)!.Value;

        Assert.Equal(mia.SmallId, told.OwnerSmallId);
    }

    [Fact]
    public void A_constraint_whose_maker_has_gone_is_sent_as_a_loaded_player()
    {
        var (world, joel, kanza, mia) = KanzaStillLoading();
        using var _ = world;

        world.Spawn(joel, 300, "Pack.Spawnable.Crate", 1, 2, 3);
        joel.Send(ModuleProtocol.WriteModuleToClients(ModuleProtocol.ConstraintCreateTag, joel.SmallId,
            ConstraintPayloads.Build(joel.SmallId, new EntityEnd(300), new SceneEnd("/Level/Wall"))));
        world.Leave(joel, "Closing Connection");

        var newbie = world.Join(NewbieId, "Newbie");

        var replay = world.Transport.SentTo(newbie.Connection)
            .Single(sent => ModuleProtocol.TryReadHandlerTag(sent.Message) == ModuleProtocol.ConstraintCreateTag);

        Assert.Equal((byte?)mia.SmallId, Envelope.Read(replay.Message)!.Value.Sender);
    }

    [Fact]
    public void An_ownerless_prop_is_adopted_by_a_loaded_player()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        // Joel has been here longer but is still loading.
        world.Server.Players.Get(joel.SmallId)!.JoinedAt =
            world.Server.Players.Get(kanza.SmallId)!.JoinedAt - TimeSpan.FromMinutes(10);

        var kept = world.Server.Entities.Register(900, "spudgun1001.Payphone.Spawnable.PayphoneWall", 0, 1f, 2f, 3f);
        kept.Persistent = true;
        world.Server.Entities.SetOwner(900, null);

        world.Join(NewbieId, "Newbie");

        Assert.Equal((byte?)kanza.SmallId, world.Server.Entities.Get(900)!.OwnerSmallId);
    }

    [Fact]
    public void A_plugin_spawn_goes_to_a_loaded_player()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        world.Server.Players.Get(joel.SmallId)!.JoinedAt =
            world.Server.Players.Get(kanza.SmallId)!.JoinedAt - TimeSpan.FromMinutes(10);

        ushort id = world.Server.SpawnForPlugin("Pack.Spawnable.Crate", 0f, 0f, 0f, Array.Empty<byte>());

        Assert.Equal((byte?)kanza.SmallId, world.Server.Entities.Get(id)!.OwnerSmallId);
    }
}
