using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A spawn carries its rotation as seven bytes of SerializedQuaternion, three
/// shorts for the smallest components and a byte naming the one that was dropped.
/// The server used to read those seven bytes and throw them away, then write an
/// identity rotation into the response, so every spawned item arrived upright.
/// </summary>
public class SpawnRotationTests
{
    private static readonly Quat Tilted = new(0.2706f, 0.2706f, 0.6533f, 0.6533f);

    private static byte[] IdentityBytes()
    {
        var request = FusionProtocol.BuildSpawnRequest(1, "Test.Barcode", Vec3.Zero, 7);
        return FusionProtocol.TryReadSpawnRequest(request)!.Value.Rotation;
    }

    /// <summary>Pulls the rotation back out of a response, past the spawn data before it.</summary>
    private static byte[] RotationOfResponse(byte[] response)
    {
        var reader = new FusionNetReader(response);

        reader.ReadByte();          // tag
        reader.ReadByte();          // relay type
        reader.ReadByte();          // channel
        reader.ReadNullableByte();  // sender
        reader.ReadInt32();         // payload length

        reader.ReadByte();          // owner
        reader.ReadUInt16();        // entity id
        reader.ReadString();        // barcode
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();

        return reader.ReadRaw(7).ToArray();
    }

    [Fact]
    public void A_request_carries_the_rotation_it_was_given()
    {
        var request = FusionProtocol.BuildSpawnRequest(
            1, "Test.Barcode", new Vec3(1, 2, 3), 7, rotation: Tilted);

        var parsed = FusionProtocol.TryReadSpawnRequest(request);

        Assert.NotNull(parsed);
        Assert.Equal(7, parsed!.Value.Rotation.Length);
        Assert.NotEqual(IdentityBytes(), parsed.Value.Rotation);
    }

    [Fact]
    public void The_rest_of_the_request_still_reads_correctly_around_it()
    {
        var request = FusionProtocol.BuildSpawnRequest(
            1, "Test.Barcode", new Vec3(1.5f, -2.5f, 3.5f), 42, rotation: Tilted);

        var parsed = FusionProtocol.TryReadSpawnRequest(request)!.Value;

        Assert.Equal("Test.Barcode", parsed.Barcode);
        Assert.Equal(new Vec3(1.5f, -2.5f, 3.5f), parsed.Position);
        Assert.Equal(42u, parsed.TrackerId);
    }

    [Fact]
    public void The_rotation_survives_the_trip_from_request_to_response()
    {
        var request = FusionProtocol.BuildSpawnRequest(
            1, "Test.Barcode", new Vec3(1, 2, 3), 7, rotation: Tilted);

        var parsed = FusionProtocol.TryReadSpawnRequest(request)!.Value;

        var response = FusionProtocol.BuildSpawnResponse(
            1, 1, 256, parsed.Barcode, parsed.Position, parsed.Rotation, parsed.TrackerId);

        Assert.Equal(parsed.Rotation, RotationOfResponse(response));
    }

    [Fact]
    public void A_response_with_no_rotation_falls_back_to_upright()
    {
        var response = FusionProtocol.BuildSpawnResponse(
            1, 1, 256, "Test.Barcode", Vec3.Zero, Array.Empty<byte>(), 7);

        Assert.Equal(IdentityBytes(), RotationOfResponse(response));
    }

    [Fact]
    public void A_rotation_of_the_wrong_length_is_refused_rather_than_shifting_the_payload()
    {
        // Writing a short rotation straight through would move every later field.
        var response = FusionProtocol.BuildSpawnResponse(
            1, 1, 256, "Test.Barcode", Vec3.Zero, new byte[] { 1, 2, 3 }, 7);

        Assert.Equal(IdentityBytes(), RotationOfResponse(response));
    }

    [Fact]
    public void A_truncated_request_reads_as_nothing_rather_than_throwing()
    {
        var request = FusionProtocol.BuildSpawnRequest(
            1, "Test.Barcode", new Vec3(1, 2, 3), 7, rotation: Tilted);

        Assert.Null(FusionProtocol.TryReadSpawnRequest(request[..(request.Length - 6)]));
    }
}
