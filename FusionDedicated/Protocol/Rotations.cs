namespace BonelabServerBrowser.Fusion;

/// <summary>Quaternion maths for the seven byte rotation a spawn carries.</summary>
public static class Rotations
{
    /// <summary>Reads a SerializedQuaternion, the reverse of FusionProtocol's writer.</summary>
    /// <returns>Null when the length is not seven or the dropped index is past the fourth component.</returns>
    public static Quat? TryDecode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != FusionProtocol.RotationBytes || bytes[FusionProtocol.RotationBytes - 1] > 3)
        {
            return null;
        }

        int largest = bytes[FusionProtocol.RotationBytes - 1];
        var components = new float[4];
        var reader = new FusionNetReader(bytes);
        float sum = 0f;

        for (var i = 0; i < 4; i++)
        {
            if (i == largest)
            {
                continue;
            }

            components[i] = reader.ReadInt16() / 10000f;
            sum += components[i] * components[i];
        }

        // The writer made the dropped component positive.
        components[largest] = MathF.Sqrt(MathF.Max(0f, 1f - sum));

        return new Quat(components[0], components[1], components[2], components[3]).Normalized;
    }

    public static byte[] Encode(Quat rotation)
    {
        var writer = new FusionNetWriter(FusionProtocol.RotationBytes);
        FusionProtocol.WriteSerializedQuaternion(writer, rotation);
        return writer.ToArray();
    }

    /// <summary>The rotation that turns by <paramref name="b"/> first and then by <paramref name="a"/>.</summary>
    public static Quat Multiply(Quat a, Quat b)
        => new(
            (a.W * b.X) + (a.X * b.W) + (a.Y * b.Z) - (a.Z * b.Y),
            (a.W * b.Y) - (a.X * b.Z) + (a.Y * b.W) + (a.Z * b.X),
            (a.W * b.Z) + (a.X * b.Y) - (a.Y * b.X) + (a.Z * b.W),
            (a.W * b.W) - (a.X * b.X) - (a.Y * b.Y) - (a.Z * b.Z));

    public static Quat Inverse(Quat q)
    {
        var n = q.Normalized;
        return new Quat(-n.X, -n.Y, -n.Z, n.W);
    }

    public static Vec3 Rotate(Quat q, Vec3 v)
    {
        var n = q.Normalized;

        // t = 2 * cross(q.xyz, v), then v + w * t + cross(q.xyz, t).
        float tx = 2f * ((n.Y * v.Z) - (n.Z * v.Y));
        float ty = 2f * ((n.Z * v.X) - (n.X * v.Z));
        float tz = 2f * ((n.X * v.Y) - (n.Y * v.X));

        return new Vec3(
            v.X + (n.W * tx) + ((n.Y * tz) - (n.Z * ty)),
            v.Y + (n.W * ty) + ((n.Z * tx) - (n.X * tz)),
            v.Z + (n.W * tz) + ((n.X * ty) - (n.Y * tx)));
    }
}
