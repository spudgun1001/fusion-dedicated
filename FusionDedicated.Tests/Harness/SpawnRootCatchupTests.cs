using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Harness;

/// <summary>
/// A joiner's copy of a car is spawned where its root is. Sent at its first body instead, a
/// multi-body prop arrived offset or turned, and its physics flung it back into place.
/// </summary>
public class SpawnRootCatchupTests
{
    private const ulong JoelId = 76561198000000001;
    private const ulong LateId = 76561198000000002;
    private const string Car = "Pack.Spawnable.Car";

    private static readonly float Half = MathF.Sqrt(0.5f);
    private static readonly Quat QuarterAboutY = new(0, Half, 0, Half);
    private static readonly Vec3 Root = new(10, 0, 10);

    /// <summary>Two metres along from the root and a quarter turn round, as a car's first body might be.</summary>
    private static readonly Vec3 Body = new(10, 0, 12);

    /// <summary>The position and rotation of a catch-up spawn, which TryReadSpawnResponse skips.</summary>
    private static (Vec3 Position, Quat Rotation) CatchUpOf(World world, FakePlayer joiner, ushort entityId)
    {
        foreach (var sent in world.Transport.SentTo(joiner.Connection))
        {
            if (FusionProtocol.TryReadSpawnResponse(sent.Message) is not { } spawn || spawn.EntityId != entityId)
            {
                continue;
            }

            var reader = new FusionNetReader(sent.Message);

            reader.ReadByte();          // tag
            reader.ReadByte();          // relay type
            reader.ReadByte();          // channel
            reader.ReadNullableByte();  // sender
            reader.ReadInt32();         // payload length

            reader.ReadByte();          // owner
            reader.ReadUInt16();        // entity id
            reader.ReadString();        // barcode

            var position = new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

            return (position, Rotations.TryDecode(reader.ReadRaw(7))!.Value);
        }

        throw new InvalidOperationException($"no catch-up spawn for {entityId}");
    }

    private static void AssertNear(Vec3 expected, Vec3 actual)
    {
        float distance = new Vec3(expected.X - actual.X, expected.Y - actual.Y, expected.Z - actual.Z).Magnitude;
        Assert.True(distance <= 0.05f, $"expected {expected}, got {actual}");
    }

    private static bool Upright(Quat rotation) => MathF.Abs(rotation.W) > 0.999f;

    /// <summary>Joel, loaded, asks for a car at the root, the way a spawn menu does.</summary>
    private static (World World, FakePlayer Joel, ushort Car) CarAtTheRoot()
    {
        var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        joel.Send(FusionProtocol.BuildSpawnRequest(joel.SmallId, Car, Root, 1, rotation: Quat.Identity));

        return (world, joel, world.Server.Entities.Entities.Single(e => e.Barcode == Car).Id);
    }

    [Fact]
    public void A_car_reaches_a_joiner_at_its_root_whatever_its_first_body_reports()
    {
        var (world, joel, car) = CarAtTheRoot();
        using var disposing = world;

        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, car, Body, QuarterAboutY, default, default));

        var spawn = CatchUpOf(world, world.Join(LateId, "Late"), car);

        AssertNear(Root, spawn.Position);
        Assert.True(Upright(spawn.Rotation), $"turned {spawn.Rotation}");
    }

    [Fact]
    public void A_crate_a_plugin_spawns_for_a_player_reaches_a_joiner_at_its_root()
    {
        using var world = new World();
        var joel = world.Join(JoelId, "Joel");
        joel.FinishLoading();

        ushort crate = world.Server.SpawnForPlayer("Pack.Spawnable.Crate", Root.X, Root.Y, Root.Z,
            Rotations.Encode(Quat.Identity), JoelId);
        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, crate, Body, QuarterAboutY, default, default));

        var spawn = CatchUpOf(world, world.Join(LateId, "Late"), crate);

        AssertNear(Root, spawn.Position);
        Assert.True(Upright(spawn.Rotation), $"turned {spawn.Rotation}");
    }

    [Fact]
    public void A_plugin_spawn_with_nobody_here_remembers_its_root()
    {
        using var world = new World();

        ushort id = world.Server.SpawnForPlugin("Pack.Spawnable.Crate", Root.X, Root.Y, Root.Z, Array.Empty<byte>());

        var root = world.Server.Entities.Get(id)!.SpawnRoot;
        Assert.NotNull(root);
        Assert.Equal(Root, root!.Value.Position);
    }

    [Fact]
    public void A_kept_car_still_goes_where_it_was_kept()
    {
        var (world, joel, car) = CarAtTheRoot();
        using var disposing = world;
        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, car, Body, QuarterAboutY, default, default));

        var kept = world.Server.Entities.Get(car)!;
        kept.Persistent = true;
        kept.KeptAt = (1f, 2f, 3f);
        kept.KeptRotation = Rotations.Encode(Quat.Identity);

        var spawn = CatchUpOf(world, world.Join(LateId, "Late"), car);

        AssertNear(new Vec3(1, 2, 3), spawn.Position);
    }

    [Fact]
    public void A_first_pose_long_after_the_spawn_leaves_a_joiner_at_the_body()
    {
        var (world, joel, car) = CarAtTheRoot();
        using var disposing = world;

        world.Advance(TimeSpan.FromSeconds(11));
        joel.Send(FusionProtocol.BuildEntityPoseUpdate(joel.SmallId, car, Body, QuarterAboutY, default, default));

        var spawn = CatchUpOf(world, world.Join(LateId, "Late"), car);

        AssertNear(Body, spawn.Position);
    }
}
