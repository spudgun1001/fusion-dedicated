using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Joiners saw guns everybody else had despawned: the server still listed them, because something
/// removed them client side without telling it. A game that has an entity answers a data request
/// for it with a pose, so when two players whose games built it both stay silent, nobody has it.
/// </summary>
public class GhostProbeTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong MiaId = 76561198000000002;
    private const ulong NewbieId = 76561198000000003;
    private const ulong LateId = 76561198000000004;
    private const ulong KanzaId = 76561198000000005;
    private const ushort Gun = 300;

    private static readonly TimeSpan PastDeadline = TimeSpan.FromSeconds(11);

    /// <summary>
    /// Joel owns a gun that his game and Mia's both built, then Newbie joins and asks Joel about it.
    /// </summary>
    private static (World World, FakePlayer Joel, FakePlayer Mia, FakePlayer Newbie) NewbieAsksJoel(
        Action<TrackedEntity>? shape = null, string barcode = "Pack.Spawnable.Gun")
    {
        var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var mia = world.Join(MiaId, "Mia");
        mia.FinishLoading();

        var gun = world.Server.Entities.Register(Gun, barcode, joel.SmallId, 1, 2, 3);
        gun.Builders.Add(joel.SmallId);
        gun.Builders.Add(mia.SmallId);
        shape?.Invoke(gun);

        var newbie = world.Join(NewbieId, "Newbie");
        newbie.FinishLoading();

        return (world, joel, mia, newbie);
    }

    private static void Answer(FakePlayer player, ushort entity, float x = 1)
        => player.Send(FusionProtocol.BuildEntityPoseUpdate(player.SmallId, entity, new Vec3(x, 2, 3), default, default, default));

    private static int DataRequestsTo(World world, FakePlayer player, ushort entity)
        => world.Transport.SentTo(player.Connection).Count(sent =>
            sent.Message[0] == FusionProtocol.TagEntityDataRequest
            && FusionProtocol.TryReadEntityDataRequest(sent.Message)?.EntityId == entity);

    private static bool IsDespawnOf(byte[] message, ushort entity)
    {
        if (Envelope.Read(message) is not { Tag: ServerProtocol.TagDespawnResponse } envelope)
        {
            return false;
        }

        var reader = new FusionNetReader(envelope.Payload);
        reader.ReadByte(); // despawner

        return reader.ReadUInt16() == entity;
    }

    private static int DespawnsTo(World world, FakePlayer player, ushort entity)
        => world.Transport.SentTo(player.Connection).Count(sent => IsDespawnOf(sent.Message, entity));

    private static List<ServerLogEntry> Lines(World world, string start)
        => world.Server.RecentLog(2000).Where(e => e.Message.StartsWith(start, StringComparison.Ordinal)).ToList();

    [Fact]
    public void An_owner_who_answers_keeps_the_entity()
    {
        var (world, joel, mia, _) = NewbieAsksJoel();
        using var _ = world;

        Answer(joel, Gun);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
    }

    /// <summary>A prop with no bodies still answers, with a pose that carries none.</summary>
    [Fact]
    public void A_bodiless_answer_keeps_the_entity()
    {
        var (world, joel, mia, newbie) = NewbieAsksJoel();
        using var _ = world;

        var payload = new FusionNetWriter(8);
        payload.WriteUInt16(Gun);
        payload.Write((byte)0);

        var message = new FusionNetWriter(32);
        message.Write(FusionProtocol.TagEntityPoseUpdate);
        message.Write((byte)4);   // ToTarget
        message.Write((byte)0);   // Reliable
        message.WriteNullable(newbie.SmallId);   // answered to the asker
        message.WriteNullable(joel.SmallId);
        message.WriteBlock(payload.ToArray());

        joel.Send(message.ToArray());
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void A_broken_answer_keeps_the_entity()
    {
        var (world, joel, mia, _) = NewbieAsksJoel();
        using var _ = world;

        Answer(joel, Gun, float.NaN);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void A_second_player_who_answers_keeps_the_entity()
    {
        var (world, joel, mia, newbie) = NewbieAsksJoel();
        using var _ = world;

        world.Advance(PastDeadline);

        var asked = world.Transport.SentTo(mia.Connection)
            .Single(sent => sent.Message[0] == FusionProtocol.TagEntityDataRequest
                            && FusionProtocol.TryReadEntityDataRequest(sent.Message)?.EntityId == Gun);
        Assert.Equal((byte?)newbie.SmallId, Envelope.Read(asked.Message)!.Value.Sender);

        Answer(mia, Gun);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DespawnsTo(world, joel, Gun));
    }

    [Fact]
    public void An_entity_neither_builder_answers_for_is_removed_everywhere()
    {
        var (world, joel, mia, newbie) = NewbieAsksJoel(gun => gun.Inherited = true);
        using var _ = world;

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.Null(world.Server.Entities.Get(Gun));
        Assert.All(new[] { joel, mia, newbie }, p => Assert.Equal(1, DespawnsTo(world, p, Gun)));

        var line = Assert.Single(Lines(world, $"Ghost removed: entity {Gun} ('Gun', Pack.Spawnable.Gun)"));
        Assert.Equal("WARN", line.Level);
        Assert.Contains($"spawned by Joel ({JoelId})", line.Message);
        Assert.Contains("owned by Joel", line.Message);
        Assert.Contains("last update 22 s ago at (1.0, 2.0, 3.0)", line.Message);
        Assert.Contains("inherited", line.Message);
        Assert.Contains("neither Joel nor Mia had it", line.Message);
    }

    [Fact]
    public void A_plugin_spawn_is_named_as_such()
    {
        var (world, _, _, _) = NewbieAsksJoel(gun => gun.PluginSpawned = true);
        using var _ = world;

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        var line = Assert.Single(Lines(world, $"Ghost removed: entity {Gun}"));
        Assert.Contains("spawned by a plugin", line.Message);
        Assert.Contains("plugin spawned", line.Message);
    }

    [Fact]
    public void With_no_other_builder_to_ask_nothing_is_removed()
    {
        var world = new World();
        using var _ = world;
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        world.Server.Entities.Register(Gun, "Pack.Spawnable.Gun", joel.SmallId, 1, 2, 3).Builders.Add(joel.SmallId);
        world.Join(NewbieId, "Newbie").FinishLoading();

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        var line = Assert.Single(Lines(world, $"Possible ghost: entity {Gun}"));
        Assert.Equal("INFO", line.Level);
    }

    /// <summary>A Quest player without the Android build, or one whose game refused the mod, never built it.</summary>
    [Fact]
    public void A_player_who_never_built_the_entity_is_never_asked()
    {
        var (world, _, mia, _) = NewbieAsksJoel(gun => gun.Builders.RemoveWhere(id => id != gun.OwnerSmallId));
        using var _ = world;

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
        Assert.Single(Lines(world, $"Possible ghost: entity {Gun}"));
    }

    [Fact]
    public void An_owner_who_never_built_it_is_passed_over_for_a_builder()
    {
        var (world, _, mia, _) = NewbieAsksJoel(gun => gun.Builders.Remove(gun.OwnerSmallId!.Value));
        using var _ = world;

        Assert.Equal(1, DataRequestsTo(world, mia, Gun));

        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Single(Lines(world, $"Possible ghost: entity {Gun}"));
    }

    [Fact]
    public void Asking_for_an_entity_or_sending_its_pose_shows_a_game_built_it()
    {
        var (world, joel, mia, newbie) = NewbieAsksJoel(gun => gun.Builders.Clear());
        using var _ = world;

        Answer(joel, Gun);

        Assert.Equal(new[] { joel.SmallId, newbie.SmallId }.Order(), world.Server.Entities.Get(Gun)!.Builders.Order());
    }

    /// <summary>Mia's fast game asks Joel before his own game has built what he spawned.</summary>
    [Fact]
    public void A_spawn_raced_by_a_faster_game_is_not_removed()
    {
        var world = new World();
        using var _ = world;
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var mia = world.Join(MiaId, "Mia");
        mia.FinishLoading();
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        world.Spawn(joel, Gun, "Pack.Spawnable.Gun", 1, 2, 3);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DespawnsTo(world, joel, Gun));
    }

    [Fact]
    public void A_departed_builders_small_id_does_not_make_a_newcomer_a_builder()
    {
        var (world, _, mia, _) = NewbieAsksJoel();
        using var _ = world;
        byte miaId = mia.SmallId;

        world.Leave(mia, "Closing Connection");

        // Loaded, but their game has not built the gun, as a Quest player without the Android build.
        var late = world.Join(LateId, "Late");
        world.Server.Players.Get(late.SmallId)!.Loaded = true;
        Assert.Equal(miaId, late.SmallId);

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, late, Gun));
    }

    [Theory]
    [InlineData("discovered")]
    [InlineData("persistent")]
    [InlineData("synthetic")]
    [InlineData("no barcode")]
    public void Level_objects_kept_props_and_barcode_less_entities_are_never_probed(string kind)
    {
        var (world, _, mia, _) = NewbieAsksJoel(
            gun =>
            {
                gun.Discovered = kind == "discovered";
                gun.Persistent = kind == "persistent";
                gun.Synthetic = kind == "synthetic";
            },
            kind is "discovered" or "no barcode" ? "" : "Pack.Spawnable.Gun");
        using var _ = world;

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void A_player_still_loading_is_never_asked_second()
    {
        var world = new World();
        using var _ = world;
        var kanza = world.Join(KanzaId, "Kanza");
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var mia = world.Join(MiaId, "Mia");
        mia.FinishLoading();
        var gun = world.Server.Entities.Register(Gun, "Pack.Spawnable.Gun", joel.SmallId, 1, 2, 3);
        gun.Builders.UnionWith(new[] { kanza.SmallId, joel.SmallId, mia.SmallId });
        world.Join(NewbieId, "Newbie").FinishLoading();

        world.Advance(PastDeadline);

        Assert.Equal(0, DataRequestsTo(world, kanza, Gun));
        Assert.Equal(1, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void Two_joiners_asking_about_one_entity_start_one_probe()
    {
        var (world, _, mia, _) = NewbieAsksJoel();
        using var _ = world;

        world.Join(LateId, "Late").FinishLoading();
        world.Advance(PastDeadline);

        Assert.Equal(1, DataRequestsTo(world, mia, Gun));
    }

    /// <summary>The gun's owner has gone, so Newbie is told it belongs to player 0, who is nobody.</summary>
    private static (World World, FakePlayer Joel, FakePlayer Mia, FakePlayer Newbie) NewbieAsksAboutAnOwnerlessGun()
        => NewbieAsksJoel(gun => gun.OwnerSmallId = null);

    [Fact]
    public void An_ownerless_entity_neither_builder_answers_for_is_removed()
    {
        var (world, joel, mia, newbie) = NewbieAsksAboutAnOwnerlessGun();
        using var _ = world;

        Assert.Equal(1, DataRequestsTo(world, joel, Gun));

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.Null(world.Server.Entities.Get(Gun));
        Assert.Equal(1, DataRequestsTo(world, mia, Gun));
        Assert.Equal(1, DespawnsTo(world, newbie, Gun));
        var line = Assert.Single(Lines(world, $"Ghost removed: entity {Gun}"));
        Assert.Contains("owned by nobody", line.Message);
        Assert.Contains("neither Joel nor Mia had it", line.Message);
    }

    [Fact]
    public void An_ownerless_entity_the_steadiest_builder_answers_for_is_kept_and_named_theirs()
    {
        var (world, joel, mia, newbie) = NewbieAsksAboutAnOwnerlessGun();
        using var _ = world;

        Answer(joel, Gun);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
        Assert.Equal((byte?)joel.SmallId, newbie.View.Entities[Gun].Owner);
        Assert.Empty(Lines(world, $"Ignored a pose for entity {Gun}"));
    }

    [Fact]
    public void An_ownerless_entity_with_only_the_joiner_here_is_left_alone()
    {
        var world = new World();
        using var _ = world;
        var gun = world.Server.Entities.Register(Gun, "Pack.Spawnable.Gun", 0, 1, 2, 3);
        gun.OwnerSmallId = null;

        var newbie = world.Join(NewbieId, "Newbie");
        newbie.FinishLoading();
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, newbie, Gun));
        Assert.DoesNotContain(world.Transport.SentTo(newbie.Connection),
            sent => FusionProtocol.TryReadOwnershipResponse(sent.Message)?.EntityId == Gun);
        Assert.Empty(Lines(world, "Possible ghost"));
    }

    [Fact]
    public void An_ownerless_entity_nobody_else_built_stays_unowned_for_the_joiner()
    {
        var (world, joel, _, newbie) = NewbieAsksJoel(gun =>
        {
            gun.OwnerSmallId = null;
            gun.Builders.Clear();
        });
        using var _ = world;

        Assert.Equal(0, DataRequestsTo(world, joel, Gun));
        Assert.Equal(PlayerRegistry.ServerSmallId, newbie.View.Entities[Gun].Owner);
    }

    [Fact]
    public void A_probe_ends_when_the_entity_changes_hands()
    {
        var (world, _, mia, _) = NewbieAsksJoel();
        using var _ = world;

        world.Server.Entities.SetOwner(Gun, mia.SmallId);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void A_probe_ends_when_the_entity_is_removed()
    {
        var (world, joel, mia, _) = NewbieAsksJoel();
        using var _ = world;

        world.Server.Entities.Remove(Gun);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
        Assert.Empty(Lines(world, "Ghost removed"));
    }

    [Fact]
    public void A_probe_ends_when_the_id_is_registered_again()
    {
        var (world, joel, mia, _) = NewbieAsksJoel();
        using var _ = world;

        world.Server.Entities.Register(Gun, "Pack.Spawnable.Other", joel.SmallId, 0, 0, 0).Builders.Add(mia.SmallId);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void A_probe_ends_when_the_requester_leaves()
    {
        var (world, _, mia, newbie) = NewbieAsksJoel();
        using var _ = world;

        world.Leave(newbie, "Closing Connection");
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void A_first_asked_player_who_leaves_moves_the_probe_on_at_once()
    {
        var (world, joel, mia, _) = NewbieAsksAboutAnOwnerlessGun();
        using var _ = world;

        world.Leave(joel, "Closing Connection");
        world.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, DataRequestsTo(world, mia, Gun));
    }

    [Fact]
    public void A_second_player_who_leaves_is_replaced()
    {
        var (world, _, mia, _) = NewbieAsksJoel();
        using var _ = world;
        var kanza = world.Join(KanzaId, "Kanza");
        kanza.FinishLoading();

        world.Advance(PastDeadline);
        Assert.Equal(1, DataRequestsTo(world, mia, Gun));

        world.Leave(mia, "Closing Connection");
        world.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(1, DataRequestsTo(world, kanza, Gun));
        Assert.NotNull(world.Server.Entities.Get(Gun));
    }
}
