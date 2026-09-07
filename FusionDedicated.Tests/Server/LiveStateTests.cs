using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Server;
using FusionDedicated.Server.Safety;

namespace FusionDedicated.Tests.Server;

/// <summary>
/// State a newcomer used to be told wrongly, because the server kept what it
/// heard at the handshake and nothing after it.
/// </summary>
public class LiveStateTests
{
    [Fact]
    public void A_pose_updates_the_rotation_as_well_as_the_position()
    {
        // The catch-up sent the rotation a thing was spawned at, however it had
        // been turned since, so it arrived on its side and righted itself only
        // once its owner answered.
        var registry = new EntityRegistry();
        var spawned = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        registry.Register(300, "Pack.Spawnable.Crate", 1, 0, 0, 0, spawned);

        var turned = new byte[] { 9, 9, 9, 9, 9, 9, 9 };
        registry.NotePose(300, 1, 5, 6, 7, turned);

        Assert.Equal(turned, registry.Get(300)!.Rotation);
        Assert.Equal(5, registry.Get(300)!.X);
    }

    [Fact]
    public void A_pose_with_no_rotation_leaves_the_one_it_had()
    {
        var registry = new EntityRegistry();
        var spawned = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        registry.Register(300, "Pack.Spawnable.Crate", 1, 0, 0, 0, spawned);

        registry.NotePose(300, 1, 5, 6, 7);

        Assert.Equal(spawned, registry.Get(300)!.Rotation);
    }

    [Fact]
    public void An_entity_pose_carries_a_rotation_a_spawn_can_repeat()
    {
        // A pose encodes rotation in four bytes and a spawn in seven, so it has
        // to be re-encoded rather than copied across.
        var pose = FusionProtocol.TryReadEntityPose(SamplePose());

        Assert.NotNull(pose);
        Assert.Equal(7, pose!.Value.Rotation.Length);
    }

    /// <summary>An EntityPoseUpdate with one body, as a client sends it.</summary>
    private static byte[] SamplePose()
    {
        var body = new FusionNetWriter(64);

        // ShortVector3: three shorts and a magnitude
        body.WriteInt16(1000);
        body.WriteInt16(0);
        body.WriteInt16(0);
        body.Write(2.5f);

        // The rotation, four signed bytes
        for (int i = 0; i < 4; i++) { body.WriteSByte(40); }

        // Velocity and angular velocity: three signed bytes and a magnitude each
        for (int i = 0; i < 2; i++)
        {
            body.WriteSByte(0);
            body.WriteSByte(0);
            body.WriteSByte(0);
            body.Write(0f);
        }

        var payload = new FusionNetWriter(96);
        payload.WriteUInt16(300);   // entity id
        payload.Write((byte)1);     // one body
        payload.WriteRaw(body.ToArray());

        var message = new FusionNetWriter(128);
        message.Write(FusionProtocol.TagEntityPoseUpdate);
        message.Write((byte)3);
        message.Write((byte)1);
        message.WriteNullable((byte)1);
        message.WriteBlock(payload.ToArray());

        return message.ToArray();
    }
}

/// <summary>
/// The spawn rate limiter is rebuilt whenever settings are pushed, which is every
/// join, leave and kick. Replacing it threw away what everybody had spent, so the
/// cap reset for the whole server whenever anybody came or went.
/// </summary>
public class RateLimitRetentionTests
{
    [Fact]
    public void Changing_the_limit_keeps_what_has_already_been_spent()
    {
        var limiter = new SpawnRateLimiter(2);
        var now = DateTime.UtcNow;

        Assert.True(limiter.Allow(3, now));
        Assert.True(limiter.Allow(3, now));
        Assert.False(limiter.Allow(3, now));

        limiter.SetLimit(2);

        Assert.False(limiter.Allow(3, now));
    }

    [Fact]
    public void A_raised_limit_takes_effect_at_once()
    {
        var limiter = new SpawnRateLimiter(1);
        var now = DateTime.UtcNow;

        Assert.True(limiter.Allow(3, now));
        Assert.False(limiter.Allow(3, now));

        limiter.SetLimit(5);

        Assert.True(limiter.Allow(3, now));
    }

    [Fact]
    public void A_nickname_guard_keeps_its_history_too()
    {
        var guard = new NicknameGuard(1, Array.Empty<string>());
        var now = DateTime.UtcNow;

        Assert.True(guard.Allow(3, "First", now).Allowed);
        Assert.False(guard.Allow(3, "Second", now).Allowed);

        guard.SetLimits(1, Array.Empty<string>());

        Assert.False(guard.Allow(3, "Third", now).Allowed);
    }

    [Fact]
    public void Reserved_names_can_still_be_changed()
    {
        var guard = new NicknameGuard(10, new[] { "Admin" });

        Assert.False(guard.Allow(3, "Admin", DateTime.UtcNow).Allowed);

        guard.SetLimits(10, Array.Empty<string>());

        Assert.True(guard.Allow(3, "Admin", DateTime.UtcNow).Allowed);
    }
}
