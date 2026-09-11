using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Reading grabs and releases as they pass through, so the server knows what each
/// player is holding. Checked against the bytes Fusion's own serializer writes.
/// </summary>
public class GrabReadTests
{
    /// <summary>A PlayerRepGrab of an entity, as Fusion writes one.</summary>
    private static byte[] FusionEntityGrab(byte relayType, byte? target, byte sender, byte hand,
        ushort gripIndex, ushort entityId)
    {
        var data = new OracleWriter();
        data.Write(hand);               // Handedness
        data.Write((byte)1);            // GrabGroup.ENTITY
        data.Write(true);               // IsGrabbed
        data.Write(0.5f);               // TargetInBase position
        data.Write(-1f);
        data.Write(2f);
        data.Write((short)0);           // TargetInBase rotation: three shorts and a byte
        data.Write((short)0);
        data.Write((short)0);
        data.Write((byte)3);
        data.Write(gripIndex);
        data.Write(entityId);

        var message = new OracleWriter();
        message.Write((byte)9);         // PlayerRepGrab
        message.Write(relayType);
        message.Write((byte)0);         // Reliable

        if (relayType == 4)
        {
            message.Write(target);      // the route's target
        }

        message.Write((byte?)sender);
        message.Write(data.ToArray());

        return message.ToArray();
    }

    [Fact]
    public void A_grab_written_by_Fusion_reads_back()
    {
        var grab = FusionProtocol.TryReadGrab(FusionEntityGrab(3, null, 3, 2, 5, 4242));

        Assert.NotNull(grab);
        Assert.Equal(2, grab!.Value.Hand);
        Assert.Equal(FusionProtocol.GrabGroupEntity, grab.Value.Group);
        Assert.True(grab.Value.IsGrabbed);
        Assert.Equal(4242, grab.Value.EntityId);
    }

    [Fact]
    public void A_grab_sent_to_one_newcomer_reads_back()
    {
        // The holder's answer to a data request goes to the one player who asked,
        // so a target sits in front of the sender.
        var grab = FusionProtocol.TryReadGrab(FusionEntityGrab(4, 7, 3, 1, 0, 300));

        Assert.Equal(1, grab!.Value.Hand);
        Assert.Equal(300, grab.Value.EntityId);
    }

    [Fact]
    public void A_grab_we_build_reads_back()
    {
        var grab = FusionProtocol.TryReadGrab(FusionProtocol.BuildGrab(
            3, FusionProtocol.Handedness.RIGHT, 5, 4242, new Vec3(1, 2, 3), Quat.Identity));

        Assert.Equal((byte)FusionProtocol.Handedness.RIGHT, grab!.Value.Hand);
        Assert.Equal(4242, grab.Value.EntityId);
    }

    [Fact]
    public void A_grab_of_the_world_names_no_entity()
    {
        var data = new OracleWriter();
        data.Write((byte)1);                        // LEFT
        data.Write((byte)3);                        // GrabGroup.WORLD
        data.Write(true);
        data.Write(new byte[19], prefixed: false);  // TargetInBase
        data.Write((byte)3);                        // grabberId

        var message = new OracleWriter();
        message.Write((byte)9);
        message.Write((byte)3);
        message.Write((byte)0);
        message.Write((byte?)3);
        message.Write(data.ToArray());

        var grab = FusionProtocol.TryReadGrab(message.ToArray());

        Assert.Equal(3, grab!.Value.Group);
        Assert.Equal(0, grab.Value.EntityId);
    }

    [Fact]
    public void A_release_written_by_Fusion_reads_back_its_hand()
    {
        var data = new OracleWriter();
        data.Write((byte)2);            // RIGHT

        var message = new OracleWriter();
        message.Write((byte)10);        // PlayerRepRelease
        message.Write((byte)3);
        message.Write((byte)0);
        message.Write((byte?)3);
        message.Write(data.ToArray());

        Assert.Equal((byte)2, FusionProtocol.TryReadRelease(message.ToArray()));
    }

    [Fact]
    public void A_release_we_build_reads_back()
        => Assert.Equal((byte)1, FusionProtocol.TryReadRelease(
            FusionProtocol.BuildRelease(3, FusionProtocol.Handedness.LEFT)));

    [Fact]
    public void Anything_else_is_refused_rather_than_throwing()
    {
        byte[] grab = FusionProtocol.BuildGrab(3, FusionProtocol.Handedness.LEFT, 0, 300);
        byte[] release = FusionProtocol.BuildRelease(3, FusionProtocol.Handedness.LEFT);

        Assert.Null(FusionProtocol.TryReadGrab(release));
        Assert.Null(FusionProtocol.TryReadRelease(grab));
        Assert.Null(FusionProtocol.TryReadGrab(grab[..^3]));
        Assert.Null(FusionProtocol.TryReadGrab(Array.Empty<byte>()));
        Assert.Null(FusionProtocol.TryReadRelease(Array.Empty<byte>()));
    }
}
