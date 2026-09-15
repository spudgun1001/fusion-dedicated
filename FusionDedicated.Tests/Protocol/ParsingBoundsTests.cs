using BonelabServerBrowser.Fusion;
using FusionDedicated.Protocol;
using FusionDedicated.Tests.Harness;

namespace FusionDedicated.Tests.Protocol;

/// <summary>Route fields a client wrote wrong are refused rather than read past the end.</summary>
public class ParsingBoundsTests
{
    [Theory]
    [InlineData(new byte[] { 250, 5, 0, 0 })]
    [InlineData(new byte[] { 250, 5, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 250, 5, 0, 0x7F, 0xFF, 0xFF, 0xFF, 1, 3 })]
    [InlineData(new byte[] { 250, 5, 0, 0xFF, 0xFF, 0xFF, 0xFF, 1, 3 })]
    [InlineData(new byte[] { 250, 5, 0, 0, 0, 0, 9, 1, 3 })]
    public void A_ToTargets_route_that_does_not_fit_is_returned_unstamped(byte[] message)
    {
        byte[] original = (byte[])message.Clone();

        byte[] stamped = ServerProtocol.StampSender(message, 3);

        Assert.Equal(original, stamped);
    }

    [Fact]
    public void A_ToTargets_route_that_fits_has_its_sender_stamped()
    {
        // Two targets, then sender 9, then an empty body.
        byte[] message = { 250, 5, 0, 0, 0, 0, 2, 7, 8, 1, 9, 0, 0, 0, 0 };

        byte[] stamped = ServerProtocol.StampSender(message, 3);

        Assert.Equal(new byte[] { 250, 5, 0, 0, 0, 0, 2, 7, 8, 1, 3, 0, 0, 0, 0 }, stamped);
    }

    [Fact]
    public void A_short_ToTargets_message_is_dropped_without_an_error()
    {
        using var world = new World();
        var sender = world.Join(1, "A");
        var other = world.Join(2, "B");
        sender.FinishLoading();
        other.FinishLoading();
        int before = world.Transport.SentTo(other.Connection).Count;

        sender.Send(new byte[] { 250, 5, 0, 0 });

        Assert.DoesNotContain(world.Server.RecentLog(2000),
            e => e.Message.StartsWith("Failed to handle a packet", StringComparison.Ordinal));
        Assert.Equal(before, world.Transport.SentTo(other.Connection).Count);
    }

    /// <summary>A module message to a list of players, with <paramref name="listed"/> id bytes after the count.</summary>
    private static byte[] ModuleToTargets(int count, int listed)
    {
        var payload = new OracleWriter();
        payload.Write(ModuleProtocol.ConstraintCreateTag);
        payload.Write(new byte[] { 9, 9 }, prefixed: false);

        var message = new OracleWriter();
        message.Write((byte)ModuleProtocol.TagModule);
        message.Write((byte)5);            // ToTargets
        message.Write((byte)0);            // channel
        message.Write(count);
        message.Write(new byte[listed], prefixed: false);
        message.Write((byte?)3);           // sender
        message.Write(payload.ToArray());

        return message.ToArray();
    }

    [Fact]
    public void A_module_message_naming_256_targets_is_read()
    {
        Assert.Equal(2200854008125858879L, ModuleProtocol.TryReadHandlerTag(ModuleToTargets(256, 256)));
    }

    [Theory]
    [InlineData(257, 257)]
    [InlineData(-1, 0)]
    public void A_module_message_with_a_targets_count_out_of_range_is_refused(int count, int listed)
    {
        Assert.Null(ModuleProtocol.TryReadHandlerTag(ModuleToTargets(count, listed)));
    }
}
