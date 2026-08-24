namespace BonelabServerBrowser.Fusion;

public record struct Vec3(float X, float Y, float Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public float Magnitude => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public Vec3 Normalized
    {
        get
        {
            float m = Magnitude;
            return m > 1e-6f ? new Vec3(X / m, Y / m, Z / m) : Zero;
        }
    }

    public override string ToString() => $"({X,6:F2},{Y,6:F2},{Z,6:F2})";
}

public record struct Quat(float X, float Y, float Z, float W)
{
    public static readonly Quat Identity = new(0, 0, 0, 1);

    public override string ToString() => $"({X,5:F2},{Y,5:F2},{Z,5:F2},{W,5:F2})";
}

/// <summary>
/// Fusion's RigPose. Only three transforms are synced — headset, left and right
/// controller (RigAbstractor.TransformSyncCount == 3).
/// </summary>
public sealed class FusionRigPose
{
    public const int Size = 122;

    public const int HeadIndex = 0;
    public const int LeftHandIndex = 1;
    public const int RightHandIndex = 2;

    public Vec3[] TrackedPositions = new Vec3[3];
    public Quat[] TrackedRotations = { Quat.Identity, Quat.Identity, Quat.Identity };

    public Quat Playspace = Quat.Identity;

    public Vec3 PelvisPosition = Vec3.Zero;
    public Quat PelvisRotation = Quat.Identity;
    public Vec3 PelvisVelocity = Vec3.Zero;
    public Vec3 PelvisAngularVelocity = Vec3.Zero;

    public byte[] LeftController = BuildController(0f, false);
    public byte[] RightController = BuildController(0f, false);

    /// <summary>
    /// Builds the 16 byte SerializedController block.
    /// Layout: 7 compressed floats (value * 255), controller type, two button bools,
    /// then a SerializedSmallVector2 thumbstick.
    /// </summary>
    public static byte[] BuildController(float trigger, bool primaryButton, float grip = 1f)
    {
        var w = new FusionNetWriter(24);

        static byte Compress(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f);

        w.Write(Compress(trigger));   // IndexCurl — the trigger finger
        w.Write(Compress(grip));      // MiddleCurl
        w.Write(Compress(grip));      // RingCurl
        w.Write(Compress(grip));      // PinkyCurl
        w.Write(Compress(grip));      // ThumbCurl
        w.Write(Compress(grip));      // SolvedGrip
        w.Write(Compress(trigger));   // PrimaryAxis — trigger pull amount

        w.Write((byte)0);             // ControllerType, one byte precision

        w.Write(primaryButton);       // PrimaryInteractionButton
        w.Write(false);               // SecondaryInteractionButton

        // ThumbstickAxis: SerializedSmallVector2 (two sbytes + magnitude)
        w.WriteSByte(0);
        w.WriteSByte(0);
        w.Write(0f);

        return w.ToArray();
    }

    public float CrouchTarget;
    public float FeetOffset;
    public float Health = 100f;
    public float MaxHealth = 100f;

    // --- compression helpers, mirroring LabFusion exactly ---

    private static short ToShort(float value) => (short)(value * 30000f);
    private static float FromShort(short value) => value / 30000f;

    private static sbyte ToSByte(float value) => (sbyte)(value * 127f);
    private static float FromSByte(sbyte value) => value / 127f;

    private static void WriteShortVector3(FusionNetWriter w, Vec3 v)
    {
        var n = v.Normalized;

        w.WriteInt16(ToShort(n.X));
        w.WriteInt16(ToShort(n.Y));
        w.WriteInt16(ToShort(n.Z));
        w.Write(v.Magnitude);
    }

    private static Vec3 ReadShortVector3(ref FusionNetReader r)
    {
        var n = new Vec3(FromShort(r.ReadInt16()), FromShort(r.ReadInt16()), FromShort(r.ReadInt16()))
            .Normalized;

        float magnitude = r.ReadSingle();

        return new Vec3(n.X * magnitude, n.Y * magnitude, n.Z * magnitude);
    }

    private static void WriteSmallVector3(FusionNetWriter w, Vec3 v)
    {
        var n = v.Normalized;

        w.WriteSByte(ToSByte(n.X));
        w.WriteSByte(ToSByte(n.Y));
        w.WriteSByte(ToSByte(n.Z));
        w.Write(v.Magnitude);
    }

    private static Vec3 ReadSmallVector3(ref FusionNetReader r)
    {
        var n = new Vec3(FromSByte(r.ReadSByte()), FromSByte(r.ReadSByte()), FromSByte(r.ReadSByte()))
            .Normalized;

        float magnitude = r.ReadSingle();

        return new Vec3(n.X * magnitude, n.Y * magnitude, n.Z * magnitude);
    }

    private static void WriteSmallQuaternion(FusionNetWriter w, Quat q)
    {
        w.WriteSByte(ToSByte(q.X));
        w.WriteSByte(ToSByte(q.Y));
        w.WriteSByte(ToSByte(q.Z));
        w.WriteSByte(ToSByte(q.W));
    }

    private static Quat ReadSmallQuaternion(ref FusionNetReader r)
    {
        return new Quat(FromSByte(r.ReadSByte()), FromSByte(r.ReadSByte()),
                        FromSByte(r.ReadSByte()), FromSByte(r.ReadSByte()));
    }

    public void Write(FusionNetWriter w)
    {
        for (var i = 0; i < 3; i++)
        {
            WriteShortVector3(w, TrackedPositions[i]);
            WriteSmallQuaternion(w, TrackedRotations[i]);
        }

        WriteSmallQuaternion(w, Playspace);

        WriteShortVector3(w, PelvisPosition);
        WriteSmallQuaternion(w, PelvisRotation);
        WriteSmallVector3(w, PelvisVelocity);
        WriteSmallVector3(w, PelvisAngularVelocity);

        w.WriteRaw(LeftController);
        w.WriteRaw(RightController);

        w.Write(CrouchTarget);
        w.Write(FeetOffset);
        w.Write(Health);
        w.Write(MaxHealth);
    }

    /// <summary>
    /// Writes a single BodyPose (28 bytes). Used both by our rig and by entities we own.
    /// </summary>
    public static void WriteBodyPose(FusionNetWriter w, Vec3 position, Quat rotation, Vec3 velocity,
        Vec3 angularVelocity)
    {
        WriteShortVector3(w, position);
        WriteSmallQuaternion(w, rotation == default ? Quat.Identity : rotation);
        WriteSmallVector3(w, velocity);
        WriteSmallVector3(w, angularVelocity);
    }

    /// <summary>
    /// Reads a single BodyPose (28 bytes): position, rotation, velocity, angular velocity.
    /// Shared with EntityPoseUpdate, which carries an array of these.
    /// </summary>
    public static (Vec3 Position, Quat Rotation, Vec3 Velocity, Vec3 AngularVelocity) ReadBodyPose(
        ref FusionNetReader r)
    {
        var position = ReadShortVector3(ref r);
        var rotation = ReadSmallQuaternion(ref r);
        var velocity = ReadSmallVector3(ref r);
        var angularVelocity = ReadSmallVector3(ref r);

        return (position, rotation, velocity, angularVelocity);
    }

    public static FusionRigPose Read(ReadOnlySpan<byte> payload)
    {
        var r = new FusionNetReader(payload);
        var pose = new FusionRigPose();

        for (var i = 0; i < 3; i++)
        {
            pose.TrackedPositions[i] = ReadShortVector3(ref r);
            pose.TrackedRotations[i] = ReadSmallQuaternion(ref r);
        }

        pose.Playspace = ReadSmallQuaternion(ref r);

        pose.PelvisPosition = ReadShortVector3(ref r);
        pose.PelvisRotation = ReadSmallQuaternion(ref r);
        pose.PelvisVelocity = ReadSmallVector3(ref r);
        pose.PelvisAngularVelocity = ReadSmallVector3(ref r);

        pose.LeftController = r.ReadRaw(16).ToArray();
        pose.RightController = r.ReadRaw(16).ToArray();

        pose.CrouchTarget = r.ReadSingle();
        pose.FeetOffset = r.ReadSingle();
        pose.Health = r.ReadSingle();
        pose.MaxHealth = r.ReadSingle();

        return pose;
    }

    public override string ToString()
    {
        return $"head={TrackedPositions[HeadIndex]} L={TrackedPositions[LeftHandIndex]} " +
               $"R={TrackedPositions[RightHandIndex]} pelvis={PelvisPosition} " +
               $"hp={Health:F0}/{MaxHealth:F0} crouch={CrouchTarget:F2} feet={FeetOffset:F2}";
    }
}
