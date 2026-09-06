using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Builds a module message the way Fusion builds one and checks ours is the same
/// bytes.
///
/// Fusion assembles it in three layers, and each is transcribed here from its own
/// source. MessagePrefix writes the native tag, then the relay type and channel as
/// one byte each, then the sender as a nullable byte, and only when the route is
/// not None. NetMessage then appends the body as a length prefixed block.
/// ModuleMessageManager puts the eight byte handler tag on the front of that body
/// and the handler's own data after it.
///
/// A module message the game silently drops looks exactly like one that was never
/// sent, so this is the layer worth pinning hardest.
/// </summary>
public class ModuleEnvelopeOracleTests
{
    private const byte NativeModuleTag = 200;
    private const byte RelayToClients = 2;
    private const byte ChannelReliable = 0;
    private const byte ServerSmallId = 0;

    /// <summary>LabRP's BalanceReason, which it writes as a single byte.</summary>
    private enum BalanceReason : byte
    {
        Unknown = 0,
        Transfer = 1,
    }

    /// <summary>MessagePrefix.Serialize, then NetMessage.Create appending the body.</summary>
    private static byte[] FusionModuleMessage(long handlerTag, byte? sender, byte[] data)
    {
        var body = new OracleWriter();
        body.Write(handlerTag);
        body.Write(data, prefixed: false);

        var message = new OracleWriter();
        message.Write(NativeModuleTag);
        message.Write(RelayToClients);
        message.Write(ChannelReliable);
        message.Write(sender);
        message.Write(body.ToArray());

        return message.ToArray();
    }

    /// <summary>BalanceUpdateData.Serialize, field for field.</summary>
    private static byte[] FusionBalanceUpdate(
        ulong platformId, long newBalance, long delta, BalanceReason reason, string detail)
    {
        var data = new OracleWriter();
        data.Write(platformId);
        data.Write(newBalance);
        data.Write(delta);
        data.Write((byte)reason);
        data.Write(detail);

        return data.ToArray();
    }

    [Fact]
    public void Our_module_envelope_is_the_bytes_Fusion_would_have_written()
    {
        var data = FusionBalanceUpdate(
            76561198000000001, 1500, -250, BalanceReason.Transfer, "paid for a taxi");

        long tag = 1234567890123456789L;

        Assert.Equal(
            FusionModuleMessage(tag, ServerSmallId, data),
            ModuleProtocol.WriteModuleToClients(tag, ServerSmallId, data));
    }

    [Fact]
    public void The_envelope_holds_up_for_every_shape_of_payload()
    {
        var random = new Random(20260906);

        for (int i = 0; i < 500; i++)
        {
            var data = new byte[random.Next(0, 400)];
            random.NextBytes(data);

            long tag = random.NextInt64(long.MinValue, long.MaxValue);
            byte sender = (byte)random.Next(256);

            Assert.Equal(
                FusionModuleMessage(tag, sender, data),
                ModuleProtocol.WriteModuleToClients(tag, sender, data));
        }
    }

    [Fact]
    public void The_handler_tag_sits_where_Fusion_looks_for_it()
    {
        // ModuleMessageManager.GetTag reads the first eight bytes of the body as a
        // big endian long, then GetBuffer hands the handler everything after them.
        var data = new byte[] { 1, 2, 3, 4, 5 };
        long tag = -6177741004168890764L;

        byte[] message = ModuleProtocol.WriteModuleToClients(tag, ServerSmallId, data);
        var reader = new FusionNetReader(message);

        Assert.Equal(NativeModuleTag, reader.ReadByte());
        Assert.Equal(RelayToClients, reader.ReadByte());
        Assert.Equal(ChannelReliable, reader.ReadByte());
        Assert.Equal(ServerSmallId, reader.ReadNullableByte());

        int bodyLength = reader.ReadInt32();
        byte[] body = reader.ReadRaw(bodyLength).ToArray();

        Assert.Equal(8 + data.Length, body.Length);
        Assert.Equal(tag, System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(body));
        Assert.Equal(data, body[8..]);
    }

    [Fact]
    public void A_balance_update_reads_back_field_for_field()
    {
        // LabRP's client refuses a balance update unless the sender is 0, which is
        // the small ID a dedicated server sends as.
        byte[] data = FusionBalanceUpdate(
            76561198000000001, 999999999999L, -1, BalanceReason.Transfer, "fine");

        byte[] message = ModuleProtocol.WriteModuleToClients(42L, ServerSmallId, data);
        var reader = new FusionNetReader(message);

        reader.ReadByte();
        reader.ReadByte();
        reader.ReadByte();

        Assert.Equal(ServerSmallId, reader.ReadNullableByte());

        byte[] body = reader.ReadRaw(reader.ReadInt32()).ToArray();
        var fusion = new OracleReader(body[8..]);

        Assert.Equal(76561198000000001UL, fusion.ReadUInt64());
        Assert.Equal(999999999999L, fusion.ReadInt64());
        Assert.Equal(-1L, fusion.ReadInt64());
        Assert.Equal((byte)BalanceReason.Transfer, fusion.ReadByte());
        Assert.Equal("fine", fusion.ReadString());
    }
}
