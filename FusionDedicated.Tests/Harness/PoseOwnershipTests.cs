using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// Only an entity's current owner may move it on the server's books. A pose from
/// anybody else is a stale or racing copy: the sender's own client throws away a
/// pose from anyone but the owner it was told about, so recording one here would
/// silently move the entity for whoever catches up next.
/// </summary>
public class PoseOwnershipTests
{
    private const ushort Crate = 300;
    private const ushort Car = 400;

    /// <summary>Pulls the position back out of a catch-up spawn, which TryReadSpawnResponse skips.</summary>
    private static Vec3? PositionOfSpawn(byte[] message, ushort entityId)
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

        return new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static Vec3? CatchUpPosition(World world, FakePlayer joiner, ushort entityId)
        => world.Transport.SentTo(joiner.Connection)
            .Select(sent => PositionOfSpawn(sent.Message, entityId))
            .FirstOrDefault(p => p != null);

    private static void AssertNear(Vec3 expected, Vec3 actual, float tolerance = 0.1f)
    {
        float distance = new Vec3(expected.X - actual.X, expected.Y - actual.Y, expected.Z - actual.Z).Magnitude;
        Assert.True(distance <= tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void A_non_owners_pose_does_not_move_the_stored_position()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);

        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, Crate, new Vec3(50, 0, 50), default, default, default));

        var stored = world.Server.Entities.Get(Crate)!;
        Assert.Equal(1f, stored.X);
        Assert.Equal(2f, stored.Y);
        Assert.Equal(3f, stored.Z);

        var late = world.Join(76561198000000003, "Late");
        late.FinishLoading();

        AssertNear(new Vec3(1, 2, 3), CatchUpPosition(world, late, Crate)!.Value);
    }

    [Fact]
    public void The_owners_pose_does_move_it()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);

        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, Crate, new Vec3(4, 5, 6), default, default, default));

        AssertNear(new Vec3(4, 5, 6), new Vec3(
            world.Server.Entities.Get(Crate)!.X,
            world.Server.Entities.Get(Crate)!.Y,
            world.Server.Entities.Get(Crate)!.Z));

        var late = world.Join(76561198000000002, "Late");
        late.FinishLoading();

        AssertNear(new Vec3(4, 5, 6), CatchUpPosition(world, late, Crate)!.Value);
    }

    [Fact]
    public void A_seated_drivers_pose_still_counts()
    {
        using var world = new World();
        var driver = world.Join(76561198000000001, "Driver");
        var passenger = world.Join(76561198000000002, "Passenger");
        driver.FinishLoading();
        passenger.FinishLoading();

        world.Spawn(driver, Car, "BaBaCorp.AssortedAutomobiles.Spawnable.SendalSopperSedan", 0, 0, 0);

        // The passenger takes the car before anyone drives it.
        passenger.Send(FusionProtocol.BuildOwnershipRequest(passenger.SmallId, Car));
        Assert.Equal(passenger.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);

        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));
        driver.Send(FusionProtocol.BuildEntityPoseUpdate(driver.SmallId, Car, new Vec3(4, 0, 4), default, default, default));

        Assert.Equal(driver.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);
        AssertNear(new Vec3(4, 0, 4), new Vec3(
            world.Server.Entities.Get(Car)!.X,
            world.Server.Entities.Get(Car)!.Y,
            world.Server.Entities.Get(Car)!.Z));
    }

    [Fact]
    public void A_seated_driver_refused_the_vehicle_does_not_resurrect_it()
    {
        // Raising the constrainer rank past Default makes ToolGate refuse a
        // barcode naming a constrainer, which is what MayHold checks a seated
        // rider against before letting them take the vehicle.
        var config = new ServerConfig { CullOrphanedEntities = false, Constrainer = PermissionLevel.Operator };
        using var world = new World(config);
        var driver = world.Join(76561198000000001, "Driver");
        var passenger = world.Join(76561198000000002, "Passenger");
        driver.FinishLoading();
        passenger.FinishLoading();

        world.Spawn(passenger, Car, "Test.Spawnable.ConstrainerCar", 0, 0, 0);
        Assert.Equal(passenger.SmallId, world.Server.Entities.Get(Car)!.OwnerSmallId);

        driver.Send(FusionProtocol.BuildSeat(driver.SmallId, Car, 0, true));
        driver.Send(FusionProtocol.BuildEntityPoseUpdate(driver.SmallId, Car, new Vec3(4, 0, 4), default, default, default));

        Assert.Null(world.Server.Entities.Get(Car));
    }

    [Fact]
    public void A_pose_for_an_id_nobody_spawned_still_registers()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, 500, new Vec3(9, 9, 9), default, default, default));

        var entity = world.Server.Entities.Get(500);

        Assert.NotNull(entity);
        Assert.True(entity!.Discovered);
        Assert.Equal((byte?)joel.SmallId, entity.OwnerSmallId);
    }

    [Fact]
    public void A_pose_for_a_known_entity_with_no_owner_does_not_move_it()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);
        world.Server.Entities.SetOwner(Crate, null);

        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, Crate, new Vec3(50, 0, 50), default, default, default));

        var stored = world.Server.Entities.Get(Crate)!;
        Assert.Equal(1f, stored.X);
        Assert.Equal(2f, stored.Y);
        Assert.Equal(3f, stored.Z);
    }

    [Fact]
    public void Two_ignored_poses_from_the_same_sender_within_ten_seconds_give_one_line()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);

        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, Crate, new Vec3(10, 0, 10), default, default, default));
        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, Crate, new Vec3(11, 0, 11), default, default, default));

        Assert.Equal(1, world.Server.RecentLog(2000).Count(e => e.Message.Contains("Ignored a pose")));
    }

    [Fact]
    public void An_ignored_pose_after_ten_seconds_gives_a_second_line()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        var kanza = world.Join(76561198000000002, "Kanza");
        joel.FinishLoading();
        kanza.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 1, 2, 3);

        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, Crate, new Vec3(10, 0, 10), default, default, default));
        world.Advance(TimeSpan.FromSeconds(11));
        kanza.Send(FusionProtocol.BuildEntityPoseUpdate(kanza.SmallId, Crate, new Vec3(11, 0, 11), default, default, default));

        Assert.Equal(2, world.Server.RecentLog(2000).Count(e => e.Message.Contains("Ignored a pose")));
    }

    [Fact]
    public void A_kept_prop_moved_past_half_a_metre_is_logged_once()
    {
        using var world = new World();
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();

        world.Spawn(joel, Crate, "Pack.Spawnable.Crate", 0, 0, 0);

        var entity = world.Server.Entities.Get(Crate)!;
        entity.Persistent = true;
        entity.KeptAt = (0f, 0f, 0f);

        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, Crate, new Vec3(2, 0, 0), default, default, default));
        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, Crate, new Vec3(2.1f, 0, 0), default, default, default));

        Assert.Equal(1, world.Server.RecentLog(2000)
            .Count(e => e.Message.Contains("is") && e.Message.Contains("m from where it was kept")));
    }
}
