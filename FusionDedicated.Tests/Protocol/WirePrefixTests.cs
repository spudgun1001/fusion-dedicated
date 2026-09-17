using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A message whose prefix Fusion reads differently from the server let a client show other
/// players bytes the server never checked, so only Fusion's own canonical shape is accepted.
/// </summary>
public class WirePrefixTests
{
    private static readonly byte[] Body = { 9, 8, 7, 6 };

    /// <summary>Tag 5, then the route bytes given, then the payload length and body.</summary>
    private static byte[] Message(byte[] route, int? length = null, byte[]? body = null)
    {
        body ??= Body;
        var writer = new FusionNetWriter(64);
        writer.Write((byte)5);
        writer.WriteRaw(route);
        writer.Write(length ?? body.Length);
        writer.WriteRaw(body);

        return writer.ToArray();
    }

    public static TheoryData<string, byte[]> Canonical => new()
    {
        { "None", Message(new byte[] { 0, 0 }) },
        { "ToServer", Message(new byte[] { 1, 0, 1, 3 }) },
        { "ToClients", Message(new byte[] { 2, 1, 1, 3 }) },
        { "ToOtherClients", Message(new byte[] { 3, 0, 1, 3 }) },
        { "ToTarget", Message(new byte[] { 4, 0, 1, 2, 1, 3 }) },
        { "ToTarget with no target", Message(new byte[] { 4, 0, 0, 1, 3 }) },
        { "ToTargets", Message(new byte[] { 5, 0, 0, 0, 0, 2, 4, 6, 1, 3 }) },
        { "ToTargets with none", Message(new byte[] { 5, 0, 0, 0, 0, 0, 1, 3 }) },
    };

    public static TheoryData<string, byte[]> Malformed => new()
    {
        { "null sender", Message(new byte[] { 3, 0, 0 }) },
        { "sender HasValue 2", Message(new byte[] { 3, 0, 2, 3 }) },
        { "target HasValue 2", Message(new byte[] { 4, 0, 2, 2, 1, 3 }) },
        { "relay type 6", Message(new byte[] { 6, 0, 1, 3 }) },
        { "channel 2", Message(new byte[] { 3, 2, 1, 3 }) },
        { "negative target count", Message(new byte[] { 5, 0, 255, 255, 255, 255, 1, 3 }) },
        { "target count over 255", Message(new byte[] { 5, 0, 0, 0, 1, 0, 1, 3 }) },
        { "target count past the end", Message(new byte[] { 5, 0, 0, 0, 0, 200, 1, 3 }) },
        { "length short of the end", Message(new byte[] { 3, 0, 1, 3 }, length: 3) },
        { "length past the end", Message(new byte[] { 3, 0, 1, 3 }, length: 5) },
        { "negative length", Message(new byte[] { 3, 0, 1, 3 }, length: -1) },
        { "no payload length", new byte[] { 5, 3, 0, 1, 3, 0 } },
        { "tag only", new byte[] { 5 } },
    };

    [Theory]
    [MemberData(nameof(Canonical))]
    public void Fusion_shaped_prefixes_pass(string shape, byte[] message)
    {
        Assert.Null(WirePrefix.Problem(message));
        Assert.True(OracleMessage.NotCanonical(message) == null, shape);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Prefixes_fusion_would_read_differently_fail(string shape, byte[] message)
    {
        Assert.True(WirePrefix.Problem(message) != null, shape);
        Assert.True(OracleMessage.NotCanonical(message) != null, shape);
    }

    [Fact]
    public void Messages_the_test_clients_send_are_canonical()
    {
        Assert.Null(WirePrefix.Problem(ClientMessages.Avatar(3, "a", ClientMessages.AvatarStats())));
        Assert.Null(WirePrefix.Problem(ClientMessages.Avatar(3, "a", ClientMessages.AvatarStats(), target: 2)));
        Assert.Null(WirePrefix.Problem(ClientMessages.Metadata(3, "k", "v")));
        Assert.Null(WirePrefix.Problem(ClientMessages.Damage(3, 2, 10f)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void A_bool_is_read_as_fusion_reads_it(byte value, bool expected)
    {
        var reader = new FusionNetReader(new[] { value });

        Assert.Equal(expected, reader.ReadBool());
    }

    [Fact]
    public void A_bool_that_is_neither_0_nor_1_is_invalid()
    {
        bool threw = false;

        try
        {
            var reader = new FusionNetReader(new byte[] { 2 });
            reader.ReadBool();
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact]
    public void Stamping_a_null_sender_keeps_the_payload_where_fusion_reads_it()
    {
        byte[] stamped = ServerProtocol.StampSender(Message(new byte[] { 3, 0, 0 }), 7);

        var read = OracleMessage.Read(stamped);

        Assert.NotNull(read);
        Assert.Equal((byte)7, read.Value.Sender);
        Assert.Equal(Body, read.Value.Payload);
        Assert.Null(WirePrefix.Problem(stamped));
    }

    [Fact]
    public void Stamping_reads_a_target_as_fusion_does()
    {
        byte[] stamped = ServerProtocol.StampSender(Message(new byte[] { 4, 0, 0, 1, 3 }), 7);

        var read = OracleMessage.Read(stamped);

        Assert.NotNull(read);
        Assert.Null(read.Value.Target);
        Assert.Equal((byte)7, read.Value.Sender);
        Assert.Equal(Body, read.Value.Payload);
    }

    [Fact]
    public void A_target_with_a_hasvalue_of_2_is_not_routed()
        => Assert.Null(ServerProtocol.ReadRoute(Message(new byte[] { 4, 0, 2, 2, 1, 3 })).Target);
}
