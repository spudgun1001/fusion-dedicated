using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server.Props;

namespace FusionDedicated.Tests.Harness;

public class KeptPropTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "fusion-kept-props-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_dir, "persistent.json");

    public KeptPropTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Two_kept_props_stacked_on_top_of_each_other_both_register()
    {
        using var world = new World();
        var store = new PersistentPropStore(Path_);

        const string barcode = "DayTrip.Phone.Spawnable.Phone";

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 1f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 3f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        world.Server.Props = store;

        world.Join(1001, "Newcomer");

        var kept = world.Server.Entities.Entities
            .Count(e => e.Persistent && e.Barcode == barcode);

        Assert.Equal(2, kept);
    }

    [Fact]
    public void Two_kept_props_side_by_side_each_register_and_stay_two()
    {
        using var world = new World();
        var store = new PersistentPropStore(Path_);

        const string barcode = "DayTrip.Phone.Spawnable.Phone";

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 1f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1.2f,
            Y = 1f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        world.Server.Props = store;

        world.Join(1001, "First");

        Assert.Equal(2, world.Server.Entities.Entities.Count(e => e.Persistent && e.Barcode == barcode));

        world.Join(1002, "Second");

        Assert.Equal(2, world.Server.Entities.Entities.Count(e => e.Persistent && e.Barcode == barcode));
    }

    [Fact]
    public void A_kept_prop_that_has_settled_still_matches_its_own_record()
    {
        using var world = new World();
        var store = new PersistentPropStore(Path_);

        const string barcode = "DayTrip.Phone.Spawnable.Phone";

        store.Add(new PersistentProp
        {
            Barcode = barcode,
            Level = world.Server.Config.LevelBarcode,
            X = 1f,
            Y = 5f,
            Z = 2f,
            Rotation = "000FFF0102030A",
        });

        world.Server.Props = store;

        world.Join(1001, "First");

        var entity = world.Server.Entities.Entities.Single(e => e.Barcode == barcode);
        var owner = entity.OwnerSmallId ?? 0;

        // The way a pose moves it once it settles, falls, or is knocked, well past
        // the 0.5m tolerance.
        world.Server.Entities.NotePose(entity.Id, owner, entity.X, entity.Y - 2f, entity.Z);

        world.Join(1002, "Second");

        var kept = world.Server.Entities.Entities
            .Count(e => e.Persistent && e.Barcode == barcode);

        Assert.Equal(1, kept);
    }

    private const string Payphone = "spudgun1001.Payphone.Spawnable.PayphoneWall";

    /// <summary>The position and rotation bytes of a catch-up spawn, which TryReadSpawnResponse skips.</summary>
    private static (Vec3 Position, string Rotation)? SpawnOf(byte[] message, ushort entityId)
    {
        if (FusionProtocol.TryReadSpawnResponse(message) is not { } spawn || spawn.EntityId != entityId)
        {
            return null;
        }

        var reader = new FusionNetReader(message);

        reader.ReadByte();          // tag
        reader.ReadByte();          // relay type
        reader.ReadByte();          // channel
        reader.ReadNullableByte();  // sender
        reader.ReadInt32();         // payload length

        reader.ReadByte();          // owner
        reader.ReadUInt16();        // entity id
        reader.ReadString();        // barcode

        var position = new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        return (position, Convert.ToHexString(reader.ReadRaw(7)));
    }

    private static (Vec3 Position, string Rotation) CatchUpOf(World world, FakePlayer joiner, ushort entityId)
        => world.Transport.SentTo(joiner.Connection)
            .Select(sent => SpawnOf(sent.Message, entityId))
            .First(s => s != null)!.Value;

    private static void AssertNear(Vec3 expected, Vec3 actual)
    {
        float distance = new Vec3(expected.X - actual.X, expected.Y - actual.Y, expected.Z - actual.Z).Magnitude;
        Assert.True(distance <= 0.01f, $"expected {expected}, got {actual}");
    }

    /// <summary>One payphone kept by the store, and the player who joins first and so owns it.</summary>
    private (World World, FakePlayer First, ushort Entity) KeptPayphoneWithItsFirstOwner()
    {
        var world = new World();
        var store = new PersistentPropStore(Path_);

        store.Add(new PersistentProp
        {
            Barcode = Payphone,
            Level = world.Server.Config.LevelBarcode,
            X = 7.9f,
            Y = 3.2f,
            Z = -14.7f,
            Rotation = "0000004F000003",
        });

        world.Server.Props = store;

        var first = world.Join(1001, "First");
        var entity = world.Server.Entities.Entities.Single(e => e.Barcode == Payphone);

        Assert.Equal(first.SmallId, entity.OwnerSmallId);

        return (world, first, entity.Id);
    }

    [Fact]
    public void A_joiner_is_given_a_kept_prop_where_it_was_kept_whatever_its_owner_reports()
    {
        var (world, first, entity) = KeptPayphoneWithItsFirstOwner();
        using var _ = world;

        // The owner's game, still loading, reports the payphone 85 m away and turned.
        first.Send(FusionProtocol.BuildEntityPoseUpdate(first.SmallId, entity, new Vec3(60, 50, 40), default, default, default));

        var late = world.Join(1002, "Late");
        var spawn = CatchUpOf(world, late, entity);

        AssertNear(new Vec3(7.9f, 3.2f, -14.7f), spawn.Position);
        Assert.Equal("0000004F000003", spawn.Rotation);
    }

    [Fact]
    public void A_prop_kept_from_the_panel_is_given_to_joiners_where_it_was_kept()
    {
        using var world = new World();
        world.Server.Props = new PersistentPropStore(Path_);

        var joel = world.Join(1001, "Joel");
        joel.FinishLoading();
        world.Spawn(joel, 300, Payphone, 1, 2, 3);

        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, 300, new Vec3(4, 5, 6), default, default, default));
        Assert.True(world.Server.KeepProp(300, ""));

        // Kept props are ownerless until a join adopts them, so hand it back to move it.
        world.Server.Entities.SetOwner(300, joel.SmallId);
        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, 300, new Vec3(40, 50, 60), default, default, default));

        var late = world.Join(1002, "Late");

        AssertNear(new Vec3(4, 5, 6), CatchUpOf(world, late, 300).Position);
    }

    [Fact]
    public void Plugins_see_where_a_kept_prop_stands_and_where_it_was_kept()
    {
        var (world, first, entity) = KeptPayphoneWithItsFirstOwner();
        using var _ = world;

        first.Send(FusionProtocol.BuildEntityPoseUpdate(first.SmallId, entity, new Vec3(60, 50, 40), default, default, default));

        var found = world.Server.FindEntity(entity)!.Value;
        var listed = world.Server.AllEntities().Single(e => e.Id == entity);

        Assert.True(Math.Abs(found.X - 60f) < 0.01f, $"live X was {found.X}");
        Assert.Equal((7.9f, 3.2f, -14.7f), found.KeptAt);
        Assert.Equal((7.9f, 3.2f, -14.7f), listed.KeptAt);
    }
}
