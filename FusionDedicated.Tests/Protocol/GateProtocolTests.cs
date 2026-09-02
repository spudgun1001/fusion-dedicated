using System.Buffers.Binary;
using System.Text;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Formats confirmed by decompiling LabFusion 1.14.1 rather than guessed:
/// PlayerRepDamageData is SerializedAttack (float damage first) then a body-part
/// byte; PlayerRepAvatarData is 420 bytes of stats then a barcode string.
/// The envelope is tag, relay type, channel, a nullable sender byte, then a
/// big-endian length-prefixed payload.
/// </summary>
public class GateProtocolTests
{
    private static byte[] Envelope(byte tag, byte relayType, byte? sender, byte[] payload)
    {
        var buffer = new List<byte> { tag, relayType, 0 };

        if (relayType != 0)
        {
            if (sender is { } s)
            {
                buffer.Add(1);
                buffer.Add(s);
            }
            else
            {
                buffer.Add(0);
            }
        }

        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);

        buffer.AddRange(length);
        buffer.AddRange(payload);

        return buffer.ToArray();
    }

    private static byte[] BigEndianFloat(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(bytes, value);
        return bytes;
    }

    private static byte[] DamagePayload(float damage)
    {
        var payload = new byte[45];
        BigEndianFloat(damage).CopyTo(payload, 0);
        return payload;
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(0f)]
    [InlineData(9999f)]
    public void Damage_is_read_from_the_front_of_the_attack(float damage)
    {
        var message = Envelope(GateProtocol.TagPlayerRepDamage, 3, 7, DamagePayload(damage));

        Assert.Equal(damage, GateProtocol.TryReadDamage(message));
    }

    [Fact]
    public void Damage_without_a_sender_byte_still_parses()
    {
        var message = Envelope(GateProtocol.TagPlayerRepDamage, 3, null, DamagePayload(42f));

        Assert.Equal(42f, GateProtocol.TryReadDamage(message));
    }

    [Fact]
    public void Damage_from_an_unrouted_message_parses()
    {
        var message = Envelope(GateProtocol.TagPlayerRepDamage, 0, null, DamagePayload(5f));

        Assert.Equal(5f, GateProtocol.TryReadDamage(message));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 64 })]
    [InlineData(new byte[] { 64, 3, 0, 1, 7 })]
    public void A_truncated_damage_message_returns_null(byte[] message)
    {
        Assert.Null(GateProtocol.TryReadDamage(message));
    }

    [Fact]
    public void A_message_with_the_wrong_tag_returns_null()
    {
        var message = Envelope(GateProtocol.TagPlayerRepTeleport, 3, 7, DamagePayload(10f));

        Assert.Null(GateProtocol.TryReadDamage(message));
    }

    [Fact]
    public void An_avatar_barcode_is_read_after_the_stats_block()
    {
        const string barcode = "SLZ.BONELAB.Avatar.Ford";

        var payload = new List<byte>();
        payload.AddRange(new byte[420]);

        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, barcode.Length);
        payload.AddRange(length);
        payload.AddRange(Encoding.UTF8.GetBytes(barcode));

        var message = Envelope(GateProtocol.TagPlayerRepAvatar, 3, 7, payload.ToArray());

        Assert.Equal(barcode, GateProtocol.TryReadAvatarBarcode(message));
    }

    [Fact]
    public void A_short_avatar_message_returns_null()
    {
        var message = Envelope(GateProtocol.TagPlayerRepAvatar, 3, 7, new byte[10]);

        Assert.Null(GateProtocol.TryReadAvatarBarcode(message));
    }

    [Fact]
    public void The_tag_values_match_LabFusion()
    {
        Assert.Equal(5, GateProtocol.TagPlayerRepAvatar);
        Assert.Equal(58, GateProtocol.TagSlowMoButton);
        Assert.Equal(59, GateProtocol.TagPlayerMetadataRequest);
        Assert.Equal(64, GateProtocol.TagPlayerRepDamage);
        Assert.Equal(69, GateProtocol.TagPlayerRepTeleport);
    }
}
