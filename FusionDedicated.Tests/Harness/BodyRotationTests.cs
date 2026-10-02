using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Harness;

/// <summary>A level door is one entity with a fixed frame and a swinging leaf, so the leaf's turn is only in a later body.</summary>
public class BodyRotationTests
{
    private static byte[] TwoBodyPose(byte sender, ushort entity, Quat second)
    {
        var payload = new FusionNetWriter(96);
        payload.WriteUInt16(entity);
        payload.Write((byte)2);
        FusionRigPose.WriteBodyPose(payload, new Vec3(1, 2, 3), Quat.Identity, Vec3.Zero, Vec3.Zero);
        FusionRigPose.WriteBodyPose(payload, new Vec3(1, 2, 3), second, Vec3.Zero, Vec3.Zero);

        var message = new FusionNetWriter(128);
        message.Write(FusionProtocol.TagEntityPoseUpdate);
        message.Write((byte)3);
        message.Write((byte)1);
        message.WriteNullable(sender);
        message.WriteBlock(payload.ToArray());
        return message.ToArray();
    }

    [Fact]
    public void A_level_object_keeps_every_bodys_rotation()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        var door = world.Server.Entities.Register(700, "", joel.SmallId, 0, 0, 0);
        door.Discovered = true;

        var turned = new Quat(0f, 0.7071068f, 0f, 0.7071068f);
        joel.Send(TwoBodyPose(joel.SmallId, 700, turned));

        var rotations = world.Server.BodyRotationsOf(700);
        Assert.Equal(2, rotations.Count);
        Assert.Equal(0.7071068f, rotations[1].Y, 2);
        Assert.Equal(0.7071068f, rotations[1].W, 2);
    }

    [Fact]
    public void A_spawned_prop_keeps_no_body_rotations()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        var joel = world.Join(76561198000000001, "Joel");
        joel.FinishLoading();
        world.Server.Entities.Register(701, "", joel.SmallId, 0, 0, 0);

        joel.Send(TwoBodyPose(joel.SmallId, 701, new Quat(0f, 0.7071068f, 0f, 0.7071068f)));

        Assert.Empty(world.Server.BodyRotationsOf(701));
    }

    [Fact]
    public void An_unknown_entity_has_no_rotations()
    {
        using var world = new World(new ServerConfig { CullOrphanedEntities = false });
        Assert.Empty(world.Server.BodyRotationsOf(999));
    }
}
