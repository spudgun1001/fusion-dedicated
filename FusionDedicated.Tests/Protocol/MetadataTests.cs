using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// A player sets a metadata key on themselves and only the host passes it on, so
/// on a relay the value never left the person who set it. Anything reading
/// somebody else's metadata, which is how mods carry per-player state, saw
/// nothing at all.
/// </summary>
public class MetadataTests
{
    /// <summary>A PlayerMetadataRequest as Fusion's own serializer writes one.</summary>
    private static byte[] FusionRequest(byte player, string key, string value)
    {
        var data = new OracleWriter();
        data.Write(player);     // PlayerReference is a single byte
        data.Write(key);
        data.Write(value);

        var message = new OracleWriter();
        message.Write((byte)59);        // PlayerMetadataRequest
        message.Write((byte)1);         // ToServer
        message.Write((byte)0);         // Reliable
        message.Write((byte?)player);   // sender
        message.Write(data.ToArray());

        return message.ToArray();
    }

    [Fact]
    public void A_request_written_by_Fusion_reads_back()
    {
        var request = FusionProtocol.TryReadMetadataRequest(
            FusionRequest(3, "labrp.balance", "1500"));

        Assert.NotNull(request);
        Assert.Equal(3, request!.Value.PlayerSmallId);
        Assert.Equal("labrp.balance", request.Value.Key);
        Assert.Equal("1500", request.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a value with spaces")]
    [InlineData("日本語")]
    public void Any_value_survives_the_round_trip(string value)
    {
        // Strings are counted in bytes rather than characters, so a value that is
        // not plain ASCII is the one that catches a wrong length.
        var request = FusionProtocol.TryReadMetadataRequest(FusionRequest(1, "key", value));

        Assert.Equal(value, request!.Value.Value);
    }

    [Fact]
    public void Something_that_is_not_a_request_is_refused_rather_than_throwing()
    {
        Assert.Null(FusionProtocol.TryReadMetadataRequest(new byte[] { 59 }));
        Assert.Null(FusionProtocol.TryReadMetadataRequest(Array.Empty<byte>()));
    }

    [Fact]
    public void Our_answer_is_the_bytes_Fusion_would_have_written()
    {
        var data = new OracleWriter();
        data.Write((byte)3);
        data.Write("labrp.balance");
        data.Write("1500");

        var expected = new OracleWriter();
        expected.Write((byte)60);       // PlayerMetadataResponse
        expected.Write((byte)2);        // ToClients
        expected.Write((byte)0);        // Reliable
        expected.Write((byte?)0);       // sender: the server
        expected.Write(data.ToArray());

        Assert.Equal(expected.ToArray(),
            FusionProtocol.BuildMetadataResponse(3, "labrp.balance", "1500"));
    }

    [Fact]
    public void The_answer_goes_to_everybody_including_whoever_set_it()
    {
        // Fusion sends this ToClients, not ToOtherClients, so the setter also
        // hears it back and their own copy agrees with the room.
        var route = ServerProtocol.ReadRoute(FusionProtocol.BuildMetadataResponse(3, "k", "v"));

        Assert.Equal(2, route.RelayType);
    }

    [Fact]
    public void A_request_and_our_answer_carry_the_same_key_and_value()
    {
        var request = FusionProtocol.TryReadMetadataRequest(
            FusionRequest(5, "mod.state", "ready"))!.Value;

        byte[] answer = FusionProtocol.BuildMetadataResponse(
            request.PlayerSmallId, request.Key, request.Value);

        var fusion = new OracleReader(answer);

        fusion.ReadByte();
        fusion.ReadByte();
        fusion.ReadByte();
        fusion.ReadNullableByte();
        fusion.ReadInt32();

        Assert.Equal(5, fusion.ReadByte());
        Assert.Equal("mod.state", fusion.ReadString());
        Assert.Equal("ready", fusion.ReadString());
    }
}
