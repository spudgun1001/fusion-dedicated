using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// The seven byte rotation a spawn carries, read back and composed. A catch-up spawn is
/// sent at the crate root, which is worked out from how its first body is turned.
/// </summary>
public class RotationsTests
{
    private static readonly float Half = MathF.Sqrt(0.5f);

    /// <summary>A quarter turn about Y.</summary>
    private static readonly Quat QuarterAboutY = new(0, Half, 0, Half);

    /// <summary>A quarter turn about Z.</summary>
    private static readonly Quat QuarterAboutZ = new(0, 0, Half, Half);

    private static readonly Quat Tilted = new(0.2706f, 0.2706f, 0.6533f, 0.6533f);

    /// <summary>q and -q are the same rotation.</summary>
    private static bool SameRotation(Quat a, Quat b)
        => MathF.Abs((a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z) + (a.W * b.W)) > 0.9999f;

    private static void AssertNear(Vec3 expected, Vec3 actual)
    {
        float distance = new Vec3(expected.X - actual.X, expected.Y - actual.Y, expected.Z - actual.Z).Magnitude;
        Assert.True(distance <= 1e-4f, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void A_rotation_survives_seven_bytes()
    {
        foreach (var rotation in new[] { Quat.Identity, QuarterAboutY, Tilted, new Quat(-0.5f, 0.5f, -0.5f, -0.5f) })
        {
            var decoded = Rotations.TryDecode(Rotations.Encode(rotation));

            Assert.NotNull(decoded);
            Assert.True(SameRotation(rotation, decoded!.Value), $"{rotation} came back as {decoded}");
        }
    }

    [Fact]
    public void Encoding_writes_what_a_spawn_request_writes()
    {
        var request = FusionProtocol.BuildSpawnRequest(1, "Test.Barcode", Vec3.Zero, 7, rotation: Tilted);

        Assert.Equal(FusionProtocol.TryReadSpawnRequest(request)!.Value.Rotation, Rotations.Encode(Tilted));
    }

    [Fact]
    public void The_upright_bytes_a_request_writes_read_as_upright()
    {
        var request = FusionProtocol.BuildSpawnRequest(1, "Test.Barcode", Vec3.Zero, 7);

        var decoded = Rotations.TryDecode(FusionProtocol.TryReadSpawnRequest(request)!.Value.Rotation);

        Assert.True(SameRotation(Quat.Identity, decoded!.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(8)]
    public void A_rotation_of_the_wrong_length_reads_as_nothing(int length)
        => Assert.Null(Rotations.TryDecode(new byte[length]));

    [Fact]
    public void A_dropped_index_past_the_fourth_component_reads_as_nothing()
        => Assert.Null(Rotations.TryDecode(new byte[] { 0, 0, 0, 0, 0, 0, 4 }));

    [Fact]
    public void A_rotation_times_its_inverse_is_upright()
        => Assert.True(SameRotation(Quat.Identity, Rotations.Multiply(Tilted, Rotations.Inverse(Tilted))));

    [Fact]
    public void The_inverse_of_an_unnormalised_rotation_is_normalised()
    {
        var inverse = Rotations.Inverse(new Quat(0, 0, 0, 2));

        Assert.True(MathF.Abs(inverse.W - 1f) < 1e-5f, $"expected W near 1, got {inverse.W}");

        float length = MathF.Sqrt((inverse.X * inverse.X) + (inverse.Y * inverse.Y) + (inverse.Z * inverse.Z) + (inverse.W * inverse.W));
        Assert.True(MathF.Abs(length - 1f) < 1e-5f, $"expected unit length, got {length}");
    }

    [Fact]
    public void A_quarter_turn_about_y_takes_x_to_minus_z()
        => AssertNear(new Vec3(0, 0, -1), Rotations.Rotate(QuarterAboutY, new Vec3(1, 0, 0)));

    [Fact]
    public void A_quarter_turn_about_z_takes_x_to_y()
        => AssertNear(new Vec3(0, 1, 0), Rotations.Rotate(QuarterAboutZ, new Vec3(1, 0, 0)));

    [Fact]
    public void Upright_leaves_a_vector_alone()
        => AssertNear(new Vec3(1, 2, 3), Rotations.Rotate(Quat.Identity, new Vec3(1, 2, 3)));

    [Fact]
    public void Multiply_turns_by_the_right_hand_rotation_first()
    {
        var v = new Vec3(1, 2, 3);

        AssertNear(
            Rotations.Rotate(QuarterAboutY, Rotations.Rotate(QuarterAboutZ, v)),
            Rotations.Rotate(Rotations.Multiply(QuarterAboutY, QuarterAboutZ), v));
    }
}
