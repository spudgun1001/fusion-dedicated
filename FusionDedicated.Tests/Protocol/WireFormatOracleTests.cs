using BonelabServerBrowser.Fusion;

namespace FusionDedicated.Tests.Protocol;

/// <summary>
/// Checks our writer against Fusion's, byte for byte, over thousands of random
/// values. Reading the two implementations side by side is not the same as
/// running them side by side, and a single wrong byte is a message the game
/// drops without an error anybody would see.
/// </summary>
public class WireFormatOracleTests
{
    private const int Cases = 2000;

    private static Random Seeded() => new(20260906);

    private static void Same(byte[] ours, byte[] fusion, string what)
        => Assert.True(ours.SequenceEqual(fusion),
            $"{what}: we wrote {Convert.ToHexString(ours)}, Fusion writes {Convert.ToHexString(fusion)}");

    [Fact]
    public void Bytes_and_booleans_agree()
    {
        var random = Seeded();

        for (int i = 0; i < Cases; i++)
        {
            byte value = (byte)random.Next(256);
            bool flag = random.Next(2) == 1;

            var ours = new FusionNetWriter(16);
            ours.Write(value);
            ours.Write(flag);

            var fusion = new OracleWriter();
            fusion.Write(value);
            fusion.Write(flag);

            Same(ours.ToArray(), fusion.ToArray(), $"byte {value}, bool {flag}");
        }
    }

    [Fact]
    public void Whole_numbers_agree_at_every_width()
    {
        var random = Seeded();

        for (int i = 0; i < Cases; i++)
        {
            short i16 = (short)random.Next(short.MinValue, short.MaxValue);
            int i32 = random.Next(int.MinValue, int.MaxValue);
            ushort u16 = (ushort)random.Next(ushort.MaxValue);
            uint u32 = (uint)random.NextInt64(uint.MaxValue);
            ulong u64 = (ulong)random.NextInt64();

            var ours = new FusionNetWriter(64);
            ours.WriteInt16(i16);
            ours.Write(i32);
            ours.WriteUInt16(u16);
            ours.WriteUInt32(u32);
            ours.Write(u64);

            var fusion = new OracleWriter();
            fusion.Write(i16);
            fusion.Write(i32);
            fusion.Write(u16);
            fusion.Write(u32);
            fusion.Write(u64);

            Same(ours.ToArray(), fusion.ToArray(), $"case {i}");
        }
    }

    [Fact]
    public void Longs_agree_including_the_ones_that_overflow_a_signed_read()
    {
        // LabRP balances are signed longs and our writer takes them as unsigned.
        // The bits must land in the same order either way, negatives included.
        long[] awkward =
        {
            0, 1, -1, long.MaxValue, long.MinValue, 999999999999L, -999999999999L,
        };

        foreach (long value in awkward)
        {
            var ours = new FusionNetWriter(16);
            ours.Write(unchecked((ulong)value));

            var fusion = new OracleWriter();
            fusion.Write(value);

            Same(ours.ToArray(), fusion.ToArray(), $"long {value}");
        }
    }

    [Fact]
    public void Floats_agree_including_the_awkward_ones()
    {
        float[] awkward =
        {
            0f, -0f, 1f, -1f, float.Epsilon, float.MaxValue, float.MinValue,
            float.PositiveInfinity, float.NegativeInfinity, float.NaN, 0.1f, -1234.5678f,
        };

        foreach (float value in awkward)
        {
            var ours = new FusionNetWriter(16);
            ours.Write(value);

            var fusion = new OracleWriter();
            fusion.Write(value);

            Same(ours.ToArray(), fusion.ToArray(), $"float {value}");
        }

        var random = Seeded();

        for (int i = 0; i < Cases; i++)
        {
            float value = (float)((random.NextDouble() - 0.5) * 100000);

            var ours = new FusionNetWriter(16);
            ours.Write(value);

            var fusion = new OracleWriter();
            fusion.Write(value);

            Same(ours.ToArray(), fusion.ToArray(), $"float {value}");
        }
    }

    [Fact]
    public void Signed_bytes_agree_across_the_whole_range()
    {
        // Fusion shifts by 128 rather than reinterpreting, so every value matters.
        for (int value = sbyte.MinValue; value <= sbyte.MaxValue; value++)
        {
            var ours = new FusionNetWriter(8);
            ours.WriteSByte((sbyte)value);

            var fusion = new OracleWriter();
            fusion.Write((sbyte)value);

            Same(ours.ToArray(), fusion.ToArray(), $"sbyte {value}");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ford")]
    [InlineData("a name with spaces")]
    [InlineData("emoji and accents eau")]
    [InlineData("日本語のテキスト")]
    [InlineData(null)]
    public void Strings_agree_including_null_and_anything_multibyte(string? value)
    {
        // A UTF-8 string is prefixed with its byte count, not its character count,
        // and getting that wrong only shows up on a name nobody tested with.
        var ours = new FusionNetWriter(128);
        ours.Write(value);

        var fusion = new OracleWriter();
        fusion.Write(value);

        Same(ours.ToArray(), fusion.ToArray(), "string");
    }

    [Fact]
    public void Nullable_bytes_agree_both_ways()
    {
        foreach (byte? value in new byte?[] { null, 0, 1, 200, 255 })
        {
            var ours = new FusionNetWriter(8);
            ours.WriteNullable(value);

            var fusion = new OracleWriter();
            fusion.Write(value);

            Same(ours.ToArray(), fusion.ToArray(), $"nullable byte {value}");
        }
    }

    [Fact]
    public void Byte_blocks_agree()
    {
        var random = Seeded();

        for (int i = 0; i < 200; i++)
        {
            var block = new byte[random.Next(0, 300)];
            random.NextBytes(block);

            var ours = new FusionNetWriter(512);
            ours.WriteBlock(block);

            var fusion = new OracleWriter();
            fusion.Write(block);

            Same(ours.ToArray(), fusion.ToArray(), $"block of {block.Length}");
        }
    }

    [Fact]
    public void Dictionaries_and_lists_agree()
    {
        var map = new Dictionary<string, string>
        {
            ["level"] = "Museum",
            ["mode"] = "sandbox",
            ["blank"] = "",
        };

        var list = new List<string> { "one", "two", "", "three" };

        var ours = new FusionNetWriter(256);
        ours.Write(map);
        ours.Write(list);

        var fusion = new OracleWriter();
        fusion.Write(map);
        fusion.Write(list);

        Same(ours.ToArray(), fusion.ToArray(), "dictionary and list");
    }

    [Fact]
    public void Fusion_reads_back_everything_we_wrote()
    {
        // The other direction. Writing the same bytes is worth nothing if Fusion's
        // reader then walks them differently.
        var ours = new FusionNetWriter(256);
        ours.Write((byte)7);
        ours.Write(true);
        ours.WriteInt16(-1234);
        ours.Write(-5678);
        ours.WriteUInt16(60000);
        ours.WriteUInt32(4000000000);
        ours.Write(76561198000000001UL);
        ours.Write(3.14159f);
        ours.WriteSByte(-100);
        ours.Write("Officer Brett");
        ours.WriteNullable(null);
        ours.WriteNullable(42);

        var fusion = new OracleReader(ours.ToArray());

        Assert.Equal(7, fusion.ReadByte());
        Assert.True(fusion.ReadBoolean());
        Assert.Equal(-1234, fusion.ReadInt16());
        Assert.Equal(-5678, fusion.ReadInt32());
        Assert.Equal(60000, fusion.ReadUInt16());
        Assert.Equal(4000000000, fusion.ReadUInt32());
        Assert.Equal(76561198000000001UL, fusion.ReadUInt64());
        Assert.Equal(3.14159f, fusion.ReadSingle());
        Assert.Equal(-100, fusion.ReadSByte());
        Assert.Equal("Officer Brett", fusion.ReadString());
        Assert.Null(fusion.ReadNullableByte());
        Assert.Equal((byte)42, fusion.ReadNullableByte());
    }

    [Fact]
    public void We_read_back_everything_Fusion_wrote()
    {
        var fusion = new OracleWriter();
        fusion.Write((byte)9);
        fusion.Write(false);
        fusion.Write((short)-4321);
        fusion.Write(-8765);
        fusion.Write((ushort)65535);
        fusion.Write(123456789u);
        fusion.Write(76561198000000002UL);
        fusion.Write(-2.71828f);
        fusion.Write((sbyte)127);
        fusion.Write("Battle ready troop");
        fusion.Write((byte?)null);
        fusion.Write((byte?)200);

        var ours = new FusionNetReader(fusion.ToArray());

        Assert.Equal(9, ours.ReadByte());
        Assert.False(ours.ReadBool());
        Assert.Equal(-4321, ours.ReadInt16());
        Assert.Equal(-8765, ours.ReadInt32());
        Assert.Equal(65535, ours.ReadUInt16());
        Assert.Equal(123456789u, ours.ReadUInt32());
        Assert.Equal(76561198000000002UL, ours.ReadUInt64());
        Assert.Equal(-2.71828f, ours.ReadSingle());
        Assert.Equal(127, ours.ReadSByte());
        Assert.Equal("Battle ready troop", ours.ReadString());
        Assert.Null(ours.ReadNullableByte());
        Assert.Equal((byte)200, ours.ReadNullableByte());
    }
}
