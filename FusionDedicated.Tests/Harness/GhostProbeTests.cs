using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Joiners saw guns everybody else had despawned: the server still listed them, because something
/// removed them client side without telling it. A game that has an entity answers a data request
/// for it with a pose, so when neither the owner nor a second player answers, nobody has it.
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

    /// <summary>Joel owns a gun and Mia has loaded, then Newbie joins and asks Joel about the gun.</summary>
    private static (World World, FakePlayer Joel, FakePlayer Mia, FakePlayer Newbie) NewbieAsksJoel(
        Action<TrackedEntity>? shape = null, string barcode = "Pack.Spawnable.Gun")
    {
        var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        var mia = world.Join(MiaId, "Mia");
        mia.FinishLoading();

        var gun = world.Server.Entities.Register(Gun, barcode, joel.SmallId, 1, 2, 3);
        shape?.Invoke(gun);

        var newbie = world.Join(NewbieId, "Newbie");
        newbie.FinishLoading();

        return (world, joel, mia, newbie);
    }

    private static void Answer(FakePlayer player, ushort entity)
        => player.Send(FusionProtocol.BuildEntityPoseUpdate(player.SmallId, entity, new Vec3(1, 2, 3), default, default, default));

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
    public void An_entity_neither_player_answers_for_is_removed_everywhere()
    {
        var (world, joel, mia, newbie) = NewbieAsksJoel();
        using var _ = world;

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.Null(world.Server.Entities.Get(Gun));
        Assert.All(new[] { joel, mia, newbie }, p => Assert.Equal(1, DespawnsTo(world, p, Gun)));

        var line = Assert.Single(Lines(world, $"Ghost removed: entity {Gun} ('Gun')"));
        Assert.Equal("WARN", line.Level);
        Assert.Contains("spawned by Joel", line.Message);
        Assert.Contains("owned by Joel", line.Message);
        Assert.Contains("neither Joel nor Mia had it", line.Message);
    }

    [Fact]
    public void With_nobody_else_to_ask_nothing_is_removed()
    {
        var world = new World();
        using var _ = world;
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();
        world.Server.Entities.Register(Gun, "Pack.Spawnable.Gun", joel.SmallId, 1, 2, 3);
        world.Join(NewbieId, "Newbie").FinishLoading();

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        var line = Assert.Single(Lines(world, $"Possible ghost: entity {Gun}"));
        Assert.Equal("INFO", line.Level);
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
        world.Server.Entities.Register(Gun, "Pack.Spawnable.Gun", joel.SmallId, 1, 2, 3);
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
    public void An_ownerless_entity_neither_settled_player_answers_for_is_removed()
    {
        var (world, joel, mia, newbie) = NewbieAsksAboutAnOwnerlessGun();
        using var _ = world;

        Assert.Equal(1, DataRequestsTo(world, joel, Gun));

        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.Null(world.Server.Entities.Get(Gun));
        Assert.Equal(1, DataRequestsTo(world, mia, Gun));
        Assert.Equal(1, DespawnsTo(world, newbie, Gun));
        var line = Assert.Single(Lines(world, $"Ghost removed: entity {Gun} ('Gun')"));
        Assert.Contains("owned by nobody; neither Joel nor Mia had it", line.Message);
    }

    [Fact]
    public void An_ownerless_entity_the_steadiest_player_answers_for_is_kept_and_named_theirs()
    {
        var (world, joel, mia, newbie) = NewbieAsksAboutAnOwnerlessGun();
        using var _ = world;

        Answer(joel, Gun);
        world.Advance(PastDeadline);
        world.Advance(PastDeadline);

        Assert.NotNull(world.Server.Entities.Get(Gun));
        Assert.Equal(0, DataRequestsTo(world, mia, Gun));
        Assert.Equal((byte?)joel.SmallId, newbie.View.Entities[Gun].Owner);
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
}
